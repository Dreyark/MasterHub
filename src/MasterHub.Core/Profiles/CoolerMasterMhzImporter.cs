using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json.Nodes;
using MasterHub.Core.Actions;
using MasterHub.Core.Audio;
using MasterHub.Core.Models;

namespace MasterHub.Core.Profiles;

/// <summary>
/// Importer oficjalnych profili Cooler Master MasterHUB (.mhz).
/// Konwertuje strukturę JSON, strony, foldery, przyciski, suwaki, rolki, pokrętła i dial oraz wypakowuje grafikę.
/// </summary>
public static class CoolerMasterMhzImporter
{
    public static List<ProfileModel> Import(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            throw new FileNotFoundException($"Plik .mhz nie został odnaleziony: {filePath}");

        using var stream = File.OpenRead(filePath);
        return Import(stream, Path.GetFileName(filePath));
    }

    public static List<ProfileModel> Import(Stream stream, string fileName)
    {
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        byte[]? jsonBytes = null;
        var images = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var innerMhz = new List<(string Name, MemoryStream Data)>();

        string mediaDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MasterHub", "media", "mhz_imported");

        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name) && entry.FullName.EndsWith("/"))
                continue;

            string normalized = entry.FullName.Replace('\\', '/');
            string baseName = Path.GetFileName(normalized);

            if (normalized.StartsWith("uploadImages/", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    Directory.CreateDirectory(mediaDir);
                    string destPath = Path.Combine(mediaDir, baseName);
                    using var entryStream = entry.Open();
                    using var destStream = File.Create(destPath);
                    entryStream.CopyTo(destStream);
                    images[baseName] = destPath;
                }
                catch { }
            }
            else if (normalized.StartsWith("gifPools/", StringComparison.OrdinalIgnoreCase))
            {
                // Pomijamy pre-renderowane klatki GIF oficjalnej aplikacji
            }
            else if (baseName.EndsWith(".mhz", StringComparison.OrdinalIgnoreCase))
            {
                var ms = new MemoryStream();
                using var entryStream = entry.Open();
                entryStream.CopyTo(ms);
                ms.Position = 0;
                innerMhz.Add((baseName, ms));
            }
            else if (jsonBytes == null)
            {
                using var entryStream = entry.Open();
                using var ms = new MemoryStream();
                entryStream.CopyTo(ms);
                jsonBytes = ms.ToArray();
            }
        }

        if (innerMhz.Count > 0)
        {
            var multiProfiles = new List<ProfileModel>();
            foreach (var (innerName, innerMs) in innerMhz)
            {
                using (innerMs)
                {
                    multiProfiles.AddRange(Import(innerMs, innerName));
                }
            }
            return multiProfiles;
        }

        if (jsonBytes == null || jsonBytes.Length == 0)
            throw new InvalidOperationException("Archiwum .mhz nie zawiera danych profilu MasterHUB.");

        var root = JsonNode.Parse(jsonBytes)
            ?? throw new InvalidOperationException("Nie udało się sparsować struktury JSON profilu .mhz.");

        if (root is not JsonObject)
            throw new InvalidOperationException("Format profilu wewnątrz .mhz jest niepoprawny.");

        string profileName = ExtractProfileName(fileName);
        var profile = ConvertProfile(root, profileName, images);
        return new List<ProfileModel> { profile };
    }

    private static string ExtractProfileName(string fileName)
    {
        string stem = Path.GetFileName(fileName);
        if (stem.EndsWith(".mhz", StringComparison.OrdinalIgnoreCase))
            stem = stem[..^4];

        int idx = stem.LastIndexOf("_profile_", StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
        {
            stem = stem[(idx + "_profile_".Length)..];
        }

        stem = stem.Trim();
        if (string.IsNullOrWhiteSpace(stem))
            return "Profil MasterHUB";

        return $"{stem} (MasterHUB)";
    }

    private static ProfileModel ConvertProfile(JsonNode root, string name, Dictionary<string, string> images)
    {
        var profile = ProfileModel.CreateEmpty(name);
        profile.Pages.Clear();
        profile.Keys.Clear();

        // 1. Klawiatura (Keypad & Strony)
        var keypadNode = GetFirstModule(root, "keypad");
        if (keypadNode is JsonArray pagesArray)
        {
            for (int p = 0; p < pagesArray.Count; p++)
            {
                var pageNode = pagesArray[p];
                var page = KeyPageModel.Create(p, $"Strona {p + 1}");

                if (pageNode is JsonArray btnArray)
                {
                    for (int slot = 0; slot < btnArray.Count; slot++)
                    {
                        var btn = btnArray[slot];
                        int id = GetInt(btn, "id", slot);
                        if (id < 0 || id >= 15) continue;

                        PopulateKey(page.Keys[id], btn, images, profile);
                    }
                }

                profile.Pages.Add(page);
            }
        }

        if (profile.Pages.Count == 0)
        {
            profile.Pages.Add(KeyPageModel.Create(0, "Strona 1"));
        }

        // Pierwsza strona to klawisze główne
        profile.Keys = new List<KeyAssignmentModel>(profile.Pages[0].Keys);

        // 2. Suwaki (sliders5List)
        var slidersNode = GetFirstModule(root, "sliders5List");
        for (int i = 0; i < 5; i++)
        {
            var slider = profile.Sliders[i];
            var sNode = slidersNode?[i.ToString()];
            if (sNode != null)
            {
                string func = GetStr(sNode, "btnFunc");
                int procId = GetInt(sNode, "volumeControlProcessId", 0);
                if (func == "volume-control")
                {
                    slider.TargetType = procId != 0 ? AudioTargetType.Process : AudioTargetType.MasterPlayback;
                    slider.DisplayTarget = procId != 0 ? "Aplikacja" : "Główna głośność";
                }
                else if (func == "audio-mixer" || func == "streamlabs-audio-mixer")
                {
                    slider.TargetType = AudioTargetType.Process;
                    slider.DisplayTarget = "OBS Audio";
                }
            }
        }

        // 3. Rolki (rollerBlock)
        var rollerBlock = GetFirstModule(root, "rollerBlock");
        if (rollerBlock != null)
        {
            var left = rollerBlock["left"];
            var right = rollerBlock["right"];

            string leftFunc = GetStr(left, "btnFunc");
            if (string.IsNullOrEmpty(leftFunc)) leftFunc = GetStr(rollerBlock, "btnFunc");
            profile.Rollers[0].Action = MapRollerAction(leftFunc);

            string rightFunc = GetStr(right, "btnFunc");
            profile.Rollers[1].Action = MapRollerAction(rightFunc);
        }

        // 4. Pokrętła (knobsList): rotateB (0), rotateR (1), rotateG (2)
        var knobsNode = GetFirstModule(root, "knobsList");
        if (knobsNode != null)
        {
            string[] knobKeys = { "rotateB", "rotateR", "rotateG" };
            for (int i = 0; i < 3; i++)
            {
                var kNode = knobsNode[knobKeys[i]];
                if (kNode == null) continue;

                var knob = profile.Knobs[i];
                JsonNode? funcNode = kNode["functionList"] is JsonArray fList && fList.Count > 0 ? fList[0] : kNode;

                string func = GetStr(funcNode, "btnFunc");
                knob.RotateAction = MapRollerAction(func);
                if (knob.RotateAction == RollerActionType.MasterVolume)
                {
                    knob.PressAction = DeckActionType.MasterMute;
                }
                else if (knob.RotateAction == RollerActionType.MediaTrack)
                {
                    knob.PressAction = DeckActionType.MediaPlayPause;
                }
            }
        }

        // 5. Dial IPS (dialEncoder)
        var dialNode = GetFirstModule(root, "dialEncoder");
        if (dialNode != null)
        {
            JsonNode? funcNode = dialNode["functionList"] is JsonArray fList && fList.Count > 0 ? fList[0] : dialNode;
            string func = GetStr(funcNode, "btnFunc");
            profile.Dial.RotateAction = MapRollerAction(func);
            if (profile.Dial.RotateAction == RollerActionType.MasterVolume)
            {
                profile.Dial.PressAction = DeckActionType.MasterMute;
            }
            else if (profile.Dial.RotateAction == RollerActionType.MediaTrack)
            {
                profile.Dial.PressAction = DeckActionType.MediaPlayPause;
            }

            string displayMode = GetStr(dialNode, "lcdDisplayMode");
            if (displayMode.Equals("Clock", StringComparison.OrdinalIgnoreCase))
            {
                profile.Dial.DisplayWidget = "Clock";
            }
            else
            {
                profile.Dial.DisplayWidget = "Image";
                string img = GetStr(dialNode, "gifPath");
                if (string.IsNullOrEmpty(img)) img = GetStr(dialNode, "imageName");
                string? localPath = FindExtractedImage(img, images);
                if (!string.IsNullOrEmpty(localPath))
                {
                    profile.Dial.ImagePath = localPath;
                }
            }
        }

        return profile;
    }

    private static void PopulateKey(KeyAssignmentModel key, JsonNode? btn, Dictionary<string, string> images, ProfileModel profile)
    {
        if (btn == null) return;

        bool enableTitle = btn["enableTitle"]?.GetValue<bool>() ?? true;
        string title = GetStr(btn, "title");
        if (string.IsNullOrWhiteSpace(title)) title = GetStr(btn, "btnName");
        if (enableTitle && !string.IsNullOrWhiteSpace(title))
        {
            key.CustomTitle = title.Trim();
        }

        string? bg = ParseHexColor(btn["btnColor"]);
        if (!string.IsNullOrEmpty(bg) && !bg.Equals("#222223", StringComparison.OrdinalIgnoreCase))
        {
            key.BackgroundColorHex = bg;
        }

        string? fg = ParseHexColor(btn["btnTextColor"]);
        if (!string.IsNullOrEmpty(fg) && !fg.Equals("#FFFFFF", StringComparison.OrdinalIgnoreCase))
        {
            key.TextColorHex = fg;
        }

        string img = GetStr(btn, "gifPath");
        if (string.IsNullOrEmpty(img)) img = GetStr(btn, "img");
        string? localImg = FindExtractedImage(img, images);
        if (!string.IsNullOrEmpty(localImg))
        {
            key.ImagePath = localImg;
        }

        string btnFunc = GetStr(btn, "btnFunc");
        if (btnFunc == "new-folder")
        {
            string folderName = !string.IsNullOrWhiteSpace(key.CustomTitle) ? key.CustomTitle : $"Folder {key.Index + 1}";
            var folder = KeyFolderModel.Create(folderName);
            folder.Id = $"folder_{Guid.NewGuid():N}";

            if (btn["folder"] is JsonArray folderItems)
            {
                for (int f = 0; f < folderItems.Count; f++)
                {
                    var item = folderItems[f];
                    int fId = GetInt(item, "id", f);
                    if (fId < 0 || fId >= 15) continue;

                    string fFunc = GetStr(item, "btnFunc");
                    if (fFunc == "new-folder" || fFunc == "previous-page")
                    {
                        folder.Keys[fId].Action = DeckActionType.GoBack;
                        folder.Keys[fId].CustomTitle = "POWRÓT";
                        folder.Keys[fId].BackgroundColorHex = "#1E293B";
                    }
                    else
                    {
                        PopulateKey(folder.Keys[fId], item, images, profile);
                    }
                }
            }

            profile.Folders.Add(folder);
            key.Action = DeckActionType.OpenFolder;
            key.ActionParameter = folder.Id;
        }
        else if (btnFunc == "multi-tasking")
        {
            key.Action = DeckActionType.MultiAction;
            key.MultiActions = new List<MultiActionStepModel>();
            if (btn["actionList"] is JsonArray actList)
            {
                for (int s = 0; s < actList.Count; s++)
                {
                    var (subAct, subParam) = ConvertAction(actList[s]);
                    if (subAct != DeckActionType.None)
                    {
                        key.MultiActions.Add(new MultiActionStepModel
                        {
                            Action = subAct,
                            Parameter = subParam,
                            DelayMs = s == 0 ? 0 : 280
                        });
                    }
                }
            }
        }
        else
        {
            var (action, param) = ConvertAction(btn);
            key.Action = action;
            key.ActionParameter = param;

            if (action == DeckActionType.ObsSceneSwitch)
            {
                key.ObsSceneName = param;
            }
            else if (action == DeckActionType.OpenApplication)
            {
                key.ApplicationPath = param;
            }
            else if (action == DeckActionType.OpenWebsite)
            {
                key.Url = param;
            }
            else if (action == DeckActionType.TextPaste)
            {
                key.TextContent = param;
            }
            else if (action == DeckActionType.Hotkey && !string.IsNullOrWhiteSpace(param))
            {
                key.HotkeyKeys = param.Split('+').ToList();
            }
        }
    }

    private static (DeckActionType Action, string Parameter) ConvertAction(JsonNode? btn)
    {
        if (btn == null) return (DeckActionType.None, "");
        string f = GetStr(btn, "btnFunc");
        int option = GetInt(btn, "dropdownOption", 0);

        switch (f)
        {
            case "web-url":
                string url = GetStr(btn, "url").Trim();
                if (string.IsNullOrEmpty(url)) return (DeckActionType.None, "");
                if (!url.Contains("://")) url = "https://" + url;
                return (DeckActionType.OpenWebsite, url);

            case "app-files":
                string appPath = GetStr(btn, "appFilePath");
                string mode = GetStr(btn, "actionAppMode");
                if (mode.Equals("Close", StringComparison.OrdinalIgnoreCase))
                    return (DeckActionType.CloseApplication, appPath);
                return (DeckActionType.OpenApplication, appPath);

            case "text":
                string content = GetStr(btn, "content");
                if (btn["afterEnter"]?.GetValue<bool>() == true) content += "\n";
                return (DeckActionType.TextPaste, content);

            case "hot-key":
            case "undo-redo":
            case "switch-desktop":
            case "zoom-in-out":
            case "mh-zoom-in-out":
                string combo = ParseCombo(btn["hotkey"]);
                return (DeckActionType.Hotkey, combo);

            case "screen-capture":
                return (DeckActionType.Screenshot, "");

            case "media-track-control":
                return option switch
                {
                    2 => (DeckActionType.MediaPrevious, ""),
                    3 => (DeckActionType.MediaNext, ""),
                    5 => (DeckActionType.MediaStop, ""),
                    _ => (DeckActionType.MediaPlayPause, "")
                };

            case "volume-control":
                return option switch
                {
                    0 => (DeckActionType.VolumeUp, ""),
                    1 => (DeckActionType.VolumeDown, ""),
                    _ => (DeckActionType.MasterMute, "")
                };

            case "scroll-up-down":
                return (DeckActionType.Hotkey, option == 0 ? "PageUp" : "PageDown");

            case "record-start-stop":
                return (DeckActionType.ObsToggleRecord, "");

            case "stream":
            case "streamlabs-stream":
                return (DeckActionType.ObsToggleStream, "");

            case "streamlabs-record":
                return (DeckActionType.ObsToggleRecord, "");

            case "replay-buffer-save":
            case "replay-buffer":
                return (DeckActionType.ObsSaveReplayBuffer, "");

            case "scenes":
                return (DeckActionType.ObsSceneSwitch, GetStr(btn, "selectSceneId"));

            case "streamlabs-scenes":
                return (DeckActionType.StreamlabsSceneSwitch, GetStr(btn, "selectSceneId"));

            case "virtual-camera":
                return (DeckActionType.ObsToggleVirtualCam, "");

            case "studio-mode":
                return (DeckActionType.ObsStudioModeToggle, "");

            case "transition":
                return (DeckActionType.ObsTriggerTransition, "");

            case "discord-mute":
                return (DeckActionType.DiscordMuteToggle, "");

            case "discord-deafen":
                return (DeckActionType.DiscordDeafenToggle, "");

            case "chat-message":
                return (DeckActionType.TwitchSendMessage, GetStr(btn, "twitchMessage"));

            case "clear-chat":
                return (DeckActionType.TwitchClearChat, "");

            case "sub-chat":
                return (DeckActionType.TwitchChatSubOnly, "");

            case "emote-chat":
                return (DeckActionType.TwitchChatEmoteOnly, "");

            case "slow-chat":
                return (DeckActionType.TwitchChatSlowMode, "");

            case "create-clip":
                return (DeckActionType.TwitchCreateClip, "");

            case "create-stream-marker":
                return (DeckActionType.TwitchCreateMarker, "");

            case "clock":
                return (DeckActionType.DisplayClock, "");

            case "next-page":
                return (DeckActionType.NextPage, "");

            case "previous-page":
                return (DeckActionType.PreviousPage, "");

            default:
                if (f.StartsWith("ps-") || f.StartsWith("ai-") || f.StartsWith("pr-"))
                {
                    string c = ParseCombo(btn["hotkey"]);
                    if (!string.IsNullOrEmpty(c)) return (DeckActionType.Hotkey, c);
                }
                return (DeckActionType.None, "");
        }
    }

    private static RollerActionType MapRollerAction(string func)
    {
        return func switch
        {
            "volume-control" => RollerActionType.MasterVolume,
            "scroll-up-down" => RollerActionType.ScrollVertical,
            "media-track-control" => RollerActionType.MediaTrack,
            _ => RollerActionType.None
        };
    }

    private static string ParseCombo(JsonNode? hotkeyNode)
    {
        if (hotkeyNode == null) return "";
        if (hotkeyNode["key"] is not JsonArray keyArr || keyArr.Count == 0) return "";

        var keys = new List<string>();
        foreach (var k in keyArr)
        {
            string? name = NormalizeKeyName(k?.GetValue<string>());
            if (!string.IsNullOrEmpty(name)) keys.Add(name);
        }

        if (keys.Count == 0) return "";

        var mods = keys.Where(k => k is "Ctrl" or "Shift" or "Alt" or "Win").Distinct().ToList();
        var nonMods = keys.Where(k => k is not ("Ctrl" or "Shift" or "Alt" or "Win")).ToList();

        var result = new List<string>(mods);
        if (nonMods.Count > 0)
        {
            result.Add(nonMods.Last());
        }
        return string.Join("+", result);
    }

    private static string? NormalizeKeyName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        string n = raw.Trim().ToLowerInvariant().Replace("vk_", "").Replace("_", "").Replace(" ", "");

        return n switch
        {
            "ctrl" or "control" or "lcontrol" or "rcontrol" or "leftcontrol" or "rightcontrol" or "lctrl" or "rctrl" => "Ctrl",
            "shift" or "lshift" or "rshift" or "leftshift" or "rightshift" => "Shift",
            "alt" or "menu" or "lmenu" or "rmenu" or "lalt" or "ralt" or "leftalt" or "rightalt" => "Alt",
            "win" or "lwin" or "rwin" or "super" or "leftsuper" or "rightsuper" or "meta" or "leftwin" or "rightwin" or "cmd" => "Win",
            "add" or "plus" or "oemplus" or "equal" or "=" => "=",
            "minus" or "subtract" or "oemminus" or "-" => "-",
            "prior" or "pageup" => "PageUp",
            "next" or "pagedown" => "PageDown",
            "return" or "enter" => "Enter",
            "escape" or "esc" => "Escape",
            "back" or "backspace" => "Backspace",
            "delete" or "del" => "Delete",
            "space" or "spacebar" => "Space",
            "tab" => "Tab",
            "home" => "Home",
            "end" => "End",
            "up" or "arrowup" => "Up",
            "down" or "arrowdown" => "Down",
            "left" or "arrowleft" => "Left",
            "right" or "arrowright" => "Right",
            "snapshot" or "print" or "printscreen" => "PrintScreen",
            "audioplay" or "audiopause" or "audioplaypause" or "mediaplaypause" or "playpause" => "PlayPause",
            "audionext" or "medianexttrack" or "nexttrack" => "NextTrack",
            "audioprev" or "mediaprevtrack" or "prevtrack" => "PrevTrack",
            "audiovolup" or "volumeup" => "VolumeUp",
            "audiovoldown" or "volumedown" => "VolumeDown",
            "audiomute" or "volumemute" => "VolumeMute",
            "oemcomma" or "comma" => ",",
            "oemperiod" or "period" => ".",
            "oem1" or "semicolon" => ";",
            "oem2" or "slash" => "/",
            "oem4" or "leftbracket" => "[",
            "oem6" or "rightbracket" => "]",
            "oem7" or "quote" => "'",
            "oem5" or "backslash" => "\\",
            "oem3" or "grave" => "`",
            _ when n.StartsWith("numpad") && n.Length == 7 && char.IsDigit(n[6]) => n[6].ToString(),
            _ when n.StartsWith("f") && int.TryParse(n[1..], out int fNum) && fNum is >= 1 and <= 24 => $"F{fNum}",
            _ when raw.Trim().Length == 1 && raw.Trim() != "+" => raw.Trim().ToUpperInvariant(),
            _ => null
        };
    }

    private static JsonNode? GetFirstModule(JsonNode root, string key)
    {
        if (root[key] is JsonObject obj)
        {
            var sortedKeys = obj.Select(kv => kv.Key).OrderBy(k => k).ToList();
            foreach (var k in sortedKeys)
            {
                if (obj[k] != null) return obj[k];
            }
        }
        else if (root[key] is JsonArray arr)
        {
            return arr;
        }
        return null;
    }

    private static string GetStr(JsonNode? node, string propertyName)
    {
        if (node is JsonObject obj && obj.TryGetPropertyValue(propertyName, out var val) && val != null)
        {
            return val.GetValue<object?>()?.ToString() ?? "";
        }
        return "";
    }

    private static int GetInt(JsonNode? node, string propertyName, int defaultValue = 0)
    {
        if (node is JsonObject obj && obj.TryGetPropertyValue(propertyName, out var val) && val != null)
        {
            if (val is JsonValue jVal)
            {
                if (jVal.TryGetValue<int>(out int i)) return i;
                if (jVal.TryGetValue<double>(out double d)) return (int)d;
                if (int.TryParse(jVal.ToString(), out int parsed)) return parsed;
            }
        }
        return defaultValue;
    }

    private static string? ParseHexColor(JsonNode? colorNode)
    {
        if (colorNode is JsonObject obj)
        {
            int r = Math.Clamp(GetInt(obj, "r", 0), 0, 255);
            int g = Math.Clamp(GetInt(obj, "g", 0), 0, 255);
            int b = Math.Clamp(GetInt(obj, "b", 0), 0, 255);
            return $"#{r:X2}{g:X2}{b:X2}";
        }
        return null;
    }

    private static string? FindExtractedImage(string? imagePath, Dictionary<string, string> images)
    {
        if (string.IsNullOrWhiteSpace(imagePath)) return null;
        string baseName = Path.GetFileName(imagePath.Replace('\\', '/'));
        return images.TryGetValue(baseName, out var fullPath) ? fullPath : null;
    }
}
