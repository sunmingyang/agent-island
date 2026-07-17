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
    // Restraint is the Cadence lesson (frame study, 2026-07-18): the film
    // has NO visible press-scale anywhere — its 打击感 lives in the
    // trackpad and in content transitions. Keep the haptic, keep the
    // compression barely perceptible.
    var pressedScale: CGFloat = 0.97

    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .scaleEffect(configuration.isPressed ? pressedScale : 1)
            .animation(.spring(response: 0.26, dampingFraction: 0.8), value: configuration.isPressed)
            .onChange(of: configuration.isPressed) { pressed in
                if pressed { Haptics.tap() }
            }
    }
}

/// The share card's "physical card" illusion (Cadence E15): the card tilts
/// 2-3° toward the pointer and settles back with a soft spring on exit.
struct CardTilt: ViewModifier {
    /// The card's design size, for normalizing pointer position.
    var size: CGSize = CGSize(width: 420, height: 560)
    @State private var tilt: CGSize = .zero

    func body(content: Content) -> some View {
        content
            .rotation3DEffect(.degrees(Double(tilt.width) * 2.4), axis: (x: 0, y: 1, z: 0))
            .rotation3DEffect(.degrees(Double(-tilt.height) * 2.4), axis: (x: 1, y: 0, z: 0))
            .scaleEffect(tilt == .zero ? 1 : 1.006)
            .onContinuousHover { phase in
                switch phase {
                case .active(let p):
                    let nx = (p.x / size.width - 0.5) * 2
                    let ny = (p.y / size.height - 0.5) * 2
                    withAnimation(.easeOut(duration: 0.12)) {
                        tilt = CGSize(width: max(-1, min(1, nx)), height: max(-1, min(1, ny)))
                    }
                case .ended:
                    withAnimation(.spring(response: 0.3, dampingFraction: 0.75)) {
                        tilt = .zero
                    }
                }
            }
    }
}

/// Cadence D12-4: a single soft dip-and-recover pulse on the whole card
/// when an action lands (their drop-commit gesture; our copy/save moment).
struct SettlePulse: ViewModifier {
    var trigger: Int

    @State private var dipped = false

    func body(content: Content) -> some View {
        content
            .scaleEffect(dipped ? 0.975 : 1)
            .onChange(of: trigger) { _ in
                withAnimation(.spring(response: 0.16, dampingFraction: 0.9)) { dipped = true }
                DispatchQueue.main.asyncAfter(deadline: .now() + 0.13) {
                    withAnimation(.spring(response: 0.3, dampingFraction: 0.6)) { dipped = false }
                }
            }
    }
}
