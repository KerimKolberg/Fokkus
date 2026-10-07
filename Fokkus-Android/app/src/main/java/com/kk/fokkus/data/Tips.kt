package com.kk.fokkus.data

enum class TipKind { MICRO, BREAK, LONG_BREAK }

data class Tip(val category: String, val kind: TipKind, val title: String, val text: String) {
    val icon: String get() = Tips.icon(category)
}

object Tips {
    val categories = listOf("Hydration", "Stretching", "Eyes", "Posture", "Breathing", "Movement", "Mindset")

    fun icon(category: String) = when (category) {
        "Hydration" -> "💧"
        "Stretching" -> "🧘"
        "Eyes" -> "👀"
        "Posture" -> "🪑"
        "Breathing" -> "🌬️"
        "Movement" -> "🚶"
        else -> "🧠"
    }

    fun color(category: String): Long = when (category) {
        "Hydration" -> 0xFF60A5FA
        "Stretching" -> 0xFF34D399
        "Eyes" -> 0xFFA78BFA
        "Posture" -> 0xFFFBBF24
        "Breathing" -> 0xFF2DD4BF
        "Movement" -> 0xFFFB923C
        else -> 0xFFF472B6
    }

    private val all = listOf(
        Tip("Hydration", TipKind.MICRO, "Sip of water", "Take a few sips of water. Your brain works better hydrated."),
        Tip("Hydration", TipKind.MICRO, "Hydration check", "Is your bottle within reach? Have a sip now."),
        Tip("Stretching", TipKind.MICRO, "Neck stretch", "Slowly tilt your ear toward your shoulder for 10 seconds, then switch sides."),
        Tip("Stretching", TipKind.MICRO, "Chin tucks", "Gently pull your chin straight back, hold 3 s. Repeat 5 times to undo phone-neck."),
        Tip("Stretching", TipKind.MICRO, "Shoulder rolls", "Roll your shoulders backwards 5 times, then forwards 5 times."),
        Tip("Stretching", TipKind.MICRO, "Wrists & fingers", "Spread your fingers wide, then make a fist. 5 times. Then circle your wrists."),
        Tip("Eyes", TipKind.MICRO, "20-20-20", "Look at something about 6 m away for 20 seconds."),
        Tip("Eyes", TipKind.MICRO, "Blink break", "Blink slowly 10 times. We blink much less when we stare at screens."),
        Tip("Posture", TipKind.MICRO, "Posture check", "Feet flat, back against the chair, screen at eye level."),
        Tip("Posture", TipKind.MICRO, "Relax your face", "Drop your shoulders, unclench your jaw, soften your forehead."),
        Tip("Breathing", TipKind.MICRO, "One deep breath", "In for 4 s, hold for 4 s, out for 6 s. Then back to it."),
        Tip("Mindset", TipKind.MICRO, "Still on track?", "If your mind wandered, no drama. Gently come back to the task."),
        Tip("Mindset", TipKind.MICRO, "One thing at a time", "Close what you don't need right now."),

        Tip("Movement", TipKind.BREAK, "Stand up & walk", "Get up and walk around for a minute or two. Refill your water on the way."),
        Tip("Movement", TipKind.BREAK, "Wake up your legs", "10 squats or 15 calf raises get the blood flowing again."),
        Tip("Stretching", TipKind.BREAK, "Full-body stretch", "Reach both arms overhead, interlace your fingers and lean slowly left and right."),
        Tip("Stretching", TipKind.BREAK, "Chest opener", "Clasp your hands behind your back, lift them gently and open your chest for 20 s."),
        Tip("Stretching", TipKind.BREAK, "Hip flexors", "Step into a lunge and hold 20 s per side. Sitting tightens these."),
        Tip("Stretching", TipKind.BREAK, "Neck release", "Slowly look over each shoulder 3 times, then let your head hang forward and breathe."),
        Tip("Eyes", TipKind.BREAK, "Window time", "Look out of a window and rest your eyes on the farthest point you can see."),
        Tip("Eyes", TipKind.BREAK, "Palming", "Rub your palms warm and rest them over your closed eyes for 30 seconds."),
        Tip("Hydration", TipKind.BREAK, "Refill", "Refill your water bottle and drink a full glass."),
        Tip("Breathing", TipKind.BREAK, "Box breathing", "4 s in, 4 s hold, 4 s out, 4 s hold. Repeat 4 rounds."),
        Tip("Mindset", TipKind.BREAK, "Phone-free break", "Try not to scroll. A real break recharges you much more than social media."),
        Tip("Mindset", TipKind.BREAK, "Tidy up", "Clear your desk for one minute. A clear space is a calm space."),

        Tip("Movement", TipKind.LONG_BREAK, "Fresh air", "Go outside for a few minutes. Daylight and air reset your focus."),
        Tip("Hydration", TipKind.LONG_BREAK, "Fuel up", "Have a proper snack (fruit, nuts, yogurt) and a glass of water."),
        Tip("Movement", TipKind.LONG_BREAK, "Move for real", "Walk around the block, take some stairs or dance to one song."),
        Tip("Stretching", TipKind.LONG_BREAK, "Stretch routine", "Neck, shoulders, chest, hips, calves: 20 seconds each. Your body will thank you."),
    )

    private val recent = ArrayDeque<Tip>()

    fun next(kind: TipKind, disabled: Set<String>): Tip {
        var pool = all.filter { (it.kind == kind || (kind == TipKind.LONG_BREAK && it.kind == TipKind.BREAK)) && it.category !in disabled }
        if (pool.isEmpty()) pool = all.filter { it.kind == kind }
        var fresh = pool.filter { it !in recent }.ifEmpty { pool }
        if (kind == TipKind.LONG_BREAK) {
            val longOnes = fresh.filter { it.kind == TipKind.LONG_BREAK }
            if (longOnes.isNotEmpty() && Math.random() < 0.5) fresh = longOnes
        }
        val tip = fresh.random()
        recent.addLast(tip)
        while (recent.size > 8) recent.removeFirst()
        return tip
    }
}
