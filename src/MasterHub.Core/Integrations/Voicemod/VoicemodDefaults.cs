namespace MasterHub.Core.Integrations.Voicemod;

using System.Collections.Generic;

public static class VoicemodDefaults
{
    public static readonly IReadOnlyList<VoicemodVoiceItem> StandardVoices = new List<VoicemodVoiceItem>
    {
        new("nofx", "Czysty głos (Clean / No FX)", false),
        new("cave", "Jaskinia (Cave)", true),
        new("robot", "Robot", true),
        new("magic-chords", "Magic Chords (Autotune)", true),
        new("chipmunk", "Wiewiórka (Chipmunk)", false),
        new("titan", "Tytan (Titan)", false),
        new("demon", "Demon", false),
        new("astronaut", "Kosmonauta (Astronaut)", false),
        new("baby", "Dziecko (Baby)", false),
        new("cop", "Policjant (Police)", false),
        new("walkie-talkie", "Walkie-Talkie", false),
        new("radio", "Radio FM", false),
        new("alien", "Obcy (Alien)", false),
        new("transformer", "Transformer", false),
        new("crazy", "Szalony (Crazy)", false),
        new("deep", "Głęboki (Deep Voice)", false),
        new("helium", "Hel (Helium)", false),
        new("megaphone", "Megafon (Megaphone)", false),
        new("spooky", "Upiór (Spooky)", false),
        new("possession", "Opętanie (Possession)", false),
        new("zombie", "Zombie", false),
        new("ogre", "Ogr (Ogre)", false),
        new("blocks", "Bloki 8-bit (Blocks)", false),
        new("custom", "Własny głos (VoiceLab)", false)
    };

    public static readonly IReadOnlyList<VoicemodSoundItem> StandardSounds = new List<VoicemodSoundItem>
    {
        new("vineboom", "Vine Boom", "once", true),
        new("airhorn", "Airhorn (Trąbka)", "once", true),
        new("bruh", "Bruh", "once", true),
        new("applause", "Brawa (Applause)", "once", true),
        new("crickets", "Świerszcze (Crickets)", "once", true),
        new("sad_trombone", "Sad Trombone", "once", true),
        new("punch", "Uderzenie (Punch)", "once", true),
        new("dramatic_horn", "Dramatic Horn", "once", true),
        new("rimshot", "Ba-Dum-Tss (Rimshot)", "once", true),
        new("laugh", "Śmiech (Laugh)", "once", true),
        new("cheering", "Wiwaty (Cheering)", "once", true),
        new("quack", "Kaczka (Quack)", "once", true),
        new("error", "Dźwięk błędu (Error)", "once", true),
        new("game_over", "Game Over", "once", true)
    };
}
