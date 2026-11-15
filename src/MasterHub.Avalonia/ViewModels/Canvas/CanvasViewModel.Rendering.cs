using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasterHub.Avalonia.Services;
using MasterHub.Core.Actions;
using MasterHub.Core.Audio;
using MasterHub.Core.Integrations.Discord;
using MasterHub.Core.Integrations.HomeAssistant;
using MasterHub.Core.Integrations.Obs;
using MasterHub.Core.Integrations.Streamlabs;
using MasterHub.Core.Integrations.StreamDeck;
using MasterHub.Core.Integrations.Teams;
using MasterHub.Core.Integrations.Kick;
using MasterHub.Core.Integrations.JetKvm;
using MasterHub.Core.Integrations.Spotify;
using MasterHub.Core.Integrations.Vlc;
using MasterHub.Core.Integrations.Voicemod;
using MasterHub.Core.Integrations.Twitch;
using MasterHub.Core.Integrations.YouTube;
using MasterHub.Core.Media;
using MasterHub.Core.Models;
using MasterHub.Core.Monitoring;
using MasterHub.Core.Profiles;
using MasterHub.Core.Razer;
using MasterHub.Core.Rendering;
using MasterHub.Core.SystemControl;
using MasterHub.Core.Wooting;
using MasterHub.Core.Localization;

namespace MasterHub.Avalonia.ViewModels.Canvas;

public partial class CanvasViewModel
{

    private void OnKeyRenderedByService(int keyIndex, byte[] jpeg)
    {
        if (_animatedKeyRenderer.IsAnimating(keyIndex)) return;

        int version = _pageRenderVersion;
        Dispatcher.UIThread.Post(() =>
        {
            if (version != _pageRenderVersion) return;
            if (keyIndex >= 0 && keyIndex < Keys.Count)
            {
                Keys[keyIndex].SetRenderedJpeg(jpeg);
            }
        });
    }

    private void OnAnimatedFrameRendered(int keyIndex, int frameIndex, byte[] jpeg)
    {
        if (_isUiSuspended) return;

        int version = _pageRenderVersion;
        Dispatcher.UIThread.Post(() =>
        {
            if (_isUiSuspended) return;
            if (version != _pageRenderVersion) return;
            if (!_animatedKeyRenderer.IsAnimating(keyIndex)) return;
            if (keyIndex < 0 || keyIndex >= Keys.Count) return;

            var key = Keys[keyIndex];
            bool isStateActive = (key.IsActiveToggleState || key.IsPressed);
            string effectiveMedia = (isStateActive && key.HasActiveImage)
                ? key.ActiveImagePath
                : key.ImagePath;

            if (!MediaValidationService.IsAnimatedOrVideo(effectiveMedia)) return;

            key.SetRenderedJpeg(jpeg);
        });
    }

    private void OnKeyChanged(KeyItemViewModel key)
    {
        RefreshTimerKey(key);                 // T-13: an edited timer key follows its timer at once
        RefreshLook(key, LookSignalSource);   // T-12: an edited rule/label takes effect at once (before the demand pass)
        UpdateMonitoringDemand();
        UpdateKeyMonitoringValue(key);
        RenderKeyVisual(key, sendToHardware: true);
        ScheduleAutoSave();
    }

    public void RenderKeyVisual(KeyItemViewModel key, bool sendToHardware)
    {
        if (key == null || key.Index < 0 || key.Index >= Keys.Count) return;

        // A Hold / Double / conditional action runs on a throw-away key that carries the real key's index. If such a key were
        // drawn, its bare default look (no label, no picture) would land on the LCD of the real key. The picture of a key
        // belongs to the key, not to the gesture that ran: always draw the real key.
        key = Keys[key.Index];

        try
        {
            bool isStateActive = (key.IsActiveToggleState || key.IsPressed);
            string effectiveMedia = (isStateActive && key.HasActiveImage)
                ? key.ActiveImagePath
                : key.ImagePath;

            string effectiveFit = (isStateActive && key.HasActiveImage)
                ? key.ActiveImageFitMode
                : key.ImageFitMode;

            int effectiveScale = (isStateActive && key.HasActiveImage)
                ? key.ActiveImageScale
                : key.ImageScale;

            int effectiveOffX = (isStateActive && key.HasActiveImage)
                ? key.ActiveImageOffsetX
                : key.ImageOffsetX;

            int effectiveOffY = (isStateActive && key.HasActiveImage)
                ? key.ActiveImageOffsetY
                : key.ImageOffsetY;

            if (MediaValidationService.IsAnimatedOrVideo(effectiveMedia))
            {
                _animatedKeyRenderer.PlayMedia(key.Index, effectiveMedia, 10, effectiveFit, effectiveScale, effectiveOffX, effectiveOffY);
                return;
            }

            _animatedKeyRenderer.StopAnimation(key.Index);

            // T-12: a rule that holds may replace the label; "{cpu}"-style tokens are filled in every label
            string? baseTitle = key.AppliedLook.Title ?? key.TimerTitleOverride ?? FillTokens(key.CustomTitle);
            string? baseSubtitle = key.AppliedLook.Subtitle ?? FillTokens(key.GetEffectiveSubtitle());
            string? activeTitle = FillTokens(key.ActiveCustomTitle);
            string? activeSubtitle = FillTokens(key.ActiveCustomSubtitle);

            string? effectiveTitle = isStateActive
                ? (!string.IsNullOrEmpty(activeTitle) ? activeTitle : baseTitle)
                : baseTitle;

            string? effectiveSubtitle = isStateActive
                ? (!string.IsNullOrEmpty(activeSubtitle) ? activeSubtitle : baseSubtitle)
                : baseSubtitle;

            byte[] RenderCustom() => ButtonImageRenderer.RenderCustomKeyJpeg(
                    key.AssignedAction,
                    key.Number,
                    effectiveTitle,
                    effectiveSubtitle,
                    key.RenderBackgroundHex,
                    key.RenderTextColorHex,
                    key.ImagePath,
                    key.FontFamily,
                    key.FontSize,
                    key.IsPressed,
                    key.ActiveImagePath,
                    key.IsActiveToggleState,
                    key.RenderSubtitleColorHex,
                    key.IsBold,
                    key.IsItalic,
                    key.IsStrikeThrough,
                    activeTitle,
                    activeSubtitle,
                    key.ActiveBackgroundColorHex,
                    key.ActiveTextColorHex,
                    key.ActiveSubtitleColorHex,
                    key.ImageFitMode,
                    key.ImageScale,
                    key.ImageOffsetX,
                    key.ImageOffsetY,
                    key.ActiveImageFitMode,
                    key.ActiveImageScale,
                    key.ActiveImageOffsetX,
                    key.ActiveImageOffsetY,
                    key.IsTextWrappingEnabled,
                    titleFontFamily: key.TitleFontFamily,
                    titleFontSize: key.TitleFontSize,
                    titleIsBold: key.TitleIsBold,
                    titleIsItalic: key.TitleIsItalic,
                    titleIsStrikeThrough: key.TitleIsStrikeThrough,
                    titleIsTextWrappingEnabled: key.TitleIsTextWrappingEnabled,
                    titleAlignment: key.TitleAlignment,
                    titleVerticalPosition: key.TitleVerticalPosition,
                    titleOffsetX: key.TitleOffsetX,
                    titleOffsetY: key.TitleOffsetY,
                    subtitleFontFamily: key.SubtitleFontFamily,
                    subtitleFontSize: key.SubtitleFontSize,
                    subtitleIsBold: key.SubtitleIsBold,
                    subtitleIsItalic: key.SubtitleIsItalic,
                    subtitleIsStrikeThrough: key.SubtitleIsStrikeThrough,
                    subtitleIsTextWrappingEnabled: key.SubtitleIsTextWrappingEnabled,
                    subtitleAlignment: key.SubtitleAlignment,
                    subtitleVerticalPosition: key.SubtitleVerticalPosition,
                    subtitleOffsetX: key.SubtitleOffsetX,
                    subtitleOffsetY: key.SubtitleOffsetY);

            byte[] jpeg;
            if (key.AssignedAction == DeckActionType.StreamDeckPluginAction)
            {
                jpeg = ButtonImageRenderer.RenderStreamDeckKeyJpeg(
                    pluginImageJpeg: key.PluginDynamicImageBytes,
                    title: effectiveTitle,
                    fallbackImagePath: !string.IsNullOrEmpty(key.PluginStateImagePath) ? key.PluginStateImagePath : key.ImagePath,
                    isPressed: key.IsPressed,
                    fontFamily: !string.IsNullOrWhiteSpace(key.TitleFontFamily) ? key.TitleFontFamily : key.FontFamily,
                    fontSize: key.TitleFontSize > 0 ? key.TitleFontSize : key.FontSize,
                    isBold: key.TitleIsBold,
                    isItalic: key.TitleIsItalic,
                    textColorHex: key.TextColorHex,
                    isAlert: key.IsStreamDeckAlert,
                    isOk: key.IsStreamDeckOk,
                    isTextWrappingEnabled: key.TitleIsTextWrappingEnabled,
                    offsetX: key.TitleOffsetX,
                    offsetY: key.TitleOffsetY);
            }
            else
            {
                jpeg = RenderCustom();
            }

            if (IsPanoramicWallpaperActive && _panorama != null)
            {
                // on a wallpaper deck the key adds only its own text and icon; the compositor sends the whole panel
                _panorama.SetOverlay(key.Index, PanoramaOverlayFor(key, jpeg, RenderCustom));
                return;
            }

            if (key.LastActionFailed) jpeg = ButtonImageRenderer.WithFailureCorner(jpeg); // T-07: red corner until a press succeeds

            if (!_isUiSuspended)
            {
                key.SetRenderedJpeg(jpeg);
            }

            if (sendToHardware)
            {
                _deviceService.SetKeyImage(key.Index, jpeg);
            }
        }
        catch
        {
            // Bezpieczne tłumienie wyjątków renderowania przy nieprawidłowych parametrach
        }
    }
}
