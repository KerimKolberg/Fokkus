using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace Fokkus
{
    class Tip
    {
        public string Category, Title, Text;
        public TipKind Kind;
        public Tip(string category, TipKind kind, string title, string text) { Category = category; Kind = kind; Title = title; Text = text; }
    }

    enum TipKind { Micro, Break, LongBreak }

    static class Tips
    {
        public static readonly string[] Categories = { "Hydration", "Stretching", "Eyes", "Posture", "Breathing", "Movement", "Mindset" };

        public static string Icon(string category)
        {
            switch (category)
            {
                case "Hydration": return "\U0001F4A7";
                case "Stretching": return "\U0001F9D8";
                case "Eyes": return "\U0001F440";
                case "Posture": return "\U0001FA91";
                case "Breathing": return "\U0001F32C";
                case "Movement": return "\U0001F6B6";
                default: return "\U0001F9E0";
            }
        }

        public static Color CategoryColor(string category)
        {
            switch (category)
            {
                case "Hydration": return System.Drawing.Color.FromArgb(96, 165, 250);
                case "Stretching": return System.Drawing.Color.FromArgb(52, 211, 153);
                case "Eyes": return System.Drawing.Color.FromArgb(167, 139, 250);
                case "Posture": return System.Drawing.Color.FromArgb(251, 191, 36);
                case "Breathing": return System.Drawing.Color.FromArgb(45, 212, 191);
                case "Movement": return System.Drawing.Color.FromArgb(251, 146, 60);
                default: return System.Drawing.Color.FromArgb(244, 114, 182);
            }
        }

        static readonly Tip[] all =
        {
            // Quick ones during focus
            new Tip("Hydration", TipKind.Micro, "Sip of water", "Take a few sips of water. Your brain works better hydrated."),
            new Tip("Hydration", TipKind.Micro, "Hydration check", "Is your bottle within reach? Have a sip now."),
            new Tip("Stretching", TipKind.Micro, "Neck stretch", "Slowly tilt your ear toward your shoulder for 10 seconds, then switch sides."),
            new Tip("Stretching", TipKind.Micro, "Chin tucks", "Gently pull your chin straight back, hold 3 s. Repeat 5 times to undo screen-neck."),
            new Tip("Stretching", TipKind.Micro, "Shoulder rolls", "Roll your shoulders backwards 5 times, then forwards 5 times."),
            new Tip("Stretching", TipKind.Micro, "Wrists & fingers", "Spread your fingers wide, then make a fist. 5 times. Then circle your wrists."),
            new Tip("Eyes", TipKind.Micro, "20-20-20", "Look at something about 6 m away for 20 seconds."),
            new Tip("Eyes", TipKind.Micro, "Blink break", "Blink slowly 10 times. We blink much less when we stare at screens."),
            new Tip("Posture", TipKind.Micro, "Posture check", "Feet flat, back against the chair, screen at eye level."),
            new Tip("Posture", TipKind.Micro, "Relax your face", "Drop your shoulders, unclench your jaw, soften your forehead."),
            new Tip("Breathing", TipKind.Micro, "One deep breath", "In for 4 s, hold for 4 s, out for 6 s. Then back to it."),
            new Tip("Mindset", TipKind.Micro, "Still on track?", "If your mind wandered, no drama. Gently come back to the task."),
            new Tip("Mindset", TipKind.Micro, "One thing at a time", "Close the tabs you don't need right now."),

            // Ideas for short breaks
            new Tip("Movement", TipKind.Break, "Stand up & walk", "Get up and walk around for a minute or two. Refill your water on the way."),
            new Tip("Movement", TipKind.Break, "Wake up your legs", "10 squats or 15 calf raises get the blood flowing again."),
            new Tip("Stretching", TipKind.Break, "Full-body stretch", "Reach both arms overhead, interlace your fingers and lean slowly left and right."),
            new Tip("Stretching", TipKind.Break, "Chest opener", "Clasp your hands behind your back, lift them gently and open your chest for 20 s."),
            new Tip("Stretching", TipKind.Break, "Hip flexors", "Step into a lunge and hold 20 s per side. Sitting tightens these."),
            new Tip("Stretching", TipKind.Break, "Neck release", "Slowly look over each shoulder 3 times, then let your head hang forward and breathe."),
            new Tip("Eyes", TipKind.Break, "Window time", "Look out of a window and rest your eyes on the farthest point you can see."),
            new Tip("Eyes", TipKind.Break, "Palming", "Rub your palms warm and rest them over your closed eyes for 30 seconds."),
            new Tip("Hydration", TipKind.Break, "Refill", "Refill your water bottle and drink a full glass."),
            new Tip("Breathing", TipKind.Break, "Box breathing", "4 s in, 4 s hold, 4 s out, 4 s hold. Repeat 4 rounds."),
            new Tip("Mindset", TipKind.Break, "Phone-free break", "Try not to scroll. A real break recharges you much more than social media."),
            new Tip("Mindset", TipKind.Break, "Tidy up", "Clear your desk for one minute. A clear space is a calm space."),

            // Long breaks
            new Tip("Movement", TipKind.LongBreak, "Fresh air", "Go outside for a few minutes. Daylight and air reset your focus."),
            new Tip("Hydration", TipKind.LongBreak, "Fuel up", "Have a proper snack (fruit, nuts, yogurt) and a glass of water."),
            new Tip("Movement", TipKind.LongBreak, "Move for real", "Walk around the block, take some stairs or dance to one song."),
            new Tip("Stretching", TipKind.LongBreak, "Stretch routine", "Neck, shoulders, chest, hips, calves: 20 seconds each. Your body will thank you."),
        };

        static readonly Random rnd = new Random();
        static readonly Queue<Tip> recent = new Queue<Tip>();

        public static Tip Next(TipKind kind, ICollection<string> disabledCategories)
        {
            var pool = all.Where(t => (t.Kind == kind || (kind == TipKind.LongBreak && t.Kind == TipKind.Break)) &&
                                      (disabledCategories == null || !disabledCategories.Contains(t.Category))).ToList();
            if (pool.Count == 0) pool = all.Where(t => t.Kind == kind).ToList();
            var fresh = pool.Where(t => !recent.Contains(t)).ToList();
            if (fresh.Count == 0) fresh = pool;
            if (kind == TipKind.LongBreak)
            {
                var longOnes = fresh.Where(t => t.Kind == TipKind.LongBreak).ToList();
                if (longOnes.Count > 0 && rnd.Next(2) == 0) fresh = longOnes;
            }
            var tip = fresh[rnd.Next(fresh.Count)];
            recent.Enqueue(tip);
            while (recent.Count > 8) recent.Dequeue();
            return tip;
        }
    }
}
