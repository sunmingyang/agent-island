import AppKit
import SwiftUI

/// The press-feel system (2026-07-18, studied from the owner's Cadence
/// app): every actionable control compresses under the pointer with a
/// quick spring and answers through the trackpad. One style + one haptics
/// helper applied app-wide, so 打击感 is a property of the system, not a
/// per-button accident.
enum Haptics {
    /// Light tick for presses and toggles.
    static func tap() {
        NSHapticFeedbackManager.defaultPerformer.perform(.levelChange, performanceTime: .now)
    }

    /// The copy/save/share "impact" — Cadence ships a dedicated
    /// ShareImpactHaptics engine for this moment; ours rides the trackpad's
    /// generic thump when the action actually lands.
    static func impact() {
        NSHapticFeedbackManager.defaultPerformer.perform(.generic, performanceTime: .drawCompleted)
    }
}

struct TactileButtonStyle: ButtonStyle {
    var pressedScale: CGFloat = 0.94

    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .scaleEffect(configuration.isPressed ? pressedScale : 1)
            .animation(.spring(response: 0.26, dampingFraction: 0.65), value: configuration.isPressed)
            .onChange(of: configuration.isPressed) { pressed in
                if pressed { Haptics.tap() }
            }
    }
}
