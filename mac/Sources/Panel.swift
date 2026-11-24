import AppKit
import Carbon.HIToolbox
import SwiftUI

@MainActor
final class AppState: ObservableObject {
    static let shared = AppState()
    @Published var fullscreen = false
    @Published var showSettings = false
    @Published var pinned = false
    @Published var focusTick = 0           // bump to refocus the text box
}

// Borderless panel that can take keyboard focus without stealing activation from other apps.
final class GlassPanel: NSPanel {
    var onEscape: (() -> Void)?
    override var canBecomeKey: Bool { true }
    override var canBecomeMain: Bool { true }
    override func cancelOperation(_ sender: Any?) { onEscape?() }
}

@MainActor
final class PanelController: NSObject, NSWindowDelegate {
    static let shared = PanelController()

    let state = AppState.shared
    private var panel: GlassPanel!
    private var side = 1                // -1 left, +1 right
    private var screen: NSScreen = NSScreen.main!
    private(set) var isShown = false
    private var animating = false
    var suppressAutoHide = false

    let width: CGFloat = 420, gap: CGFloat = 12, inset: CGFloat = 14

    func setup() {
        panel = GlassPanel(contentRect: NSRect(x: 0, y: 0, width: width, height: 700),
                           styleMask: [.borderless, .nonactivatingPanel, .fullSizeContentView],
                           backing: .buffered, defer: false)
        panel.isOpaque = false
        panel.backgroundColor = .clear
        panel.hasShadow = false          // SwiftUI draws a softer one
        panel.level = .floating
        panel.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .ignoresCycle]
        panel.isMovable = false
        panel.hidesOnDeactivate = false
        panel.isReleasedWhenClosed = false
        panel.delegate = self
        panel.onEscape = { [weak self] in self?.escape() }

        let root = RootView()
            .environmentObject(Settings.shared)
            .environmentObject(ChatModel.shared)
            .environmentObject(state)
        let host = NSHostingView(rootView: root)
        host.wantsLayer = true
        host.layer?.backgroundColor = .clear
        panel.contentView = host
    }

    // MARK: geometry (AppKit coordinates: origin bottom-left)

    private func sidebarFrame() -> NSRect {
        let v = screen.visibleFrame
        let x = side > 0 ? v.maxX - gap - width : v.minX + gap
        return NSRect(x: x, y: v.minY + gap, width: width, height: v.height - 2 * gap)
    }

    private func hiddenFrame() -> NSRect {
        var f = sidebarFrame()
        f.origin.x += CGFloat(side) * (width + gap + 40)
        return f
    }

    private func fullFrame() -> NSRect { screen.visibleFrame.insetBy(dx: inset, dy: inset) }

    private func animate(to f: NSRect, duration: Double, curve: CAMediaTimingFunction, done: (() -> Void)? = nil) {
        animating = true
        NSAnimationContext.runAnimationGroup({ ctx in
            ctx.duration = duration
            ctx.timingFunction = curve
            ctx.allowsImplicitAnimation = true
            panel.animator().setFrame(f, display: true)
        }, completionHandler: { [weak self] in
            Task { @MainActor in self?.animating = false; done?() }
        })
    }

    // snappy ease-out for appearing, softer ease-in for leaving
    private let outCurve = CAMediaTimingFunction(controlPoints: 0.16, 1.0, 0.3, 1.0)
    private let inCurve = CAMediaTimingFunction(controlPoints: 0.5, 0.0, 0.75, 0.0)

    // MARK: show / hide / fullscreen

    func show(side newSide: Int, on scr: NSScreen) {
        side = newSide
        screen = scr
        state.fullscreen = false
        panel.setFrame(hiddenFrame(), display: false)
        panel.alphaValue = 1
        panel.orderFrontRegardless()
        panel.makeKey()
        isShown = true
        animate(to: sidebarFrame(), duration: 0.46, curve: outCurve)
        state.focusTick += 1
    }

    func hide() {
        guard isShown else { return }
        isShown = false
        let finish = { [weak self] in
            guard let self else { return }
            self.animate(to: self.hiddenFrame(), duration: 0.28, curve: self.inCurve) {
                if !self.isShown { self.panel.orderOut(nil) }
            }
        }
        if state.fullscreen {
            state.fullscreen = false
            animate(to: sidebarFrame(), duration: 0.3, curve: outCurve) { finish() }
        } else { finish() }
    }

    func toggle() {
        if isShown { hide() } else { show(side: 1, on: screenUnderMouse()) }
    }

    func toggleFullscreen() {
        guard isShown else { return }
        state.fullscreen.toggle()
        animate(to: state.fullscreen ? fullFrame() : sidebarFrame(), duration: 0.5, curve: outCurve)
    }

    func onSwipe(side s: Int, screen scr: NSScreen) {
        if !isShown { show(side: s, on: scr); return }
        if state.fullscreen { return }
        if s == side { hide() } else { panel.orderOut(nil); isShown = false; show(side: s, on: scr) }
    }

    func escape() {
        if state.showSettings { state.showSettings = false; return }
        if state.fullscreen { toggleFullscreen(); return }
        hide()
    }

    func screenUnderMouse() -> NSScreen {
        let p = NSEvent.mouseLocation
        return NSScreen.screens.first { NSMouseInRect(p, $0.frame, false) } ?? NSScreen.main!
    }

    // click elsewhere → slide away, unless pinned
    func windowDidResignKey(_ notification: Notification) {
        guard isShown, !state.pinned, !suppressAutoHide else { return }
        hide()
    }
}

// MARK: - Flick detector (polls the cursor ~240 Hz; no special permissions needed)

@MainActor
final class SwipeDetector {
    private var timer: Timer?
    private var samples: [(t: Double, x: CGFloat)] = []
    private var wasAtEdge = false
    private var lastFire = 0.0
    var onSwipe: ((Int, NSScreen) -> Void)?

    func start() {
        let t = Timer(timeInterval: 1.0 / 240.0, repeats: true) { [weak self] _ in
            Task { @MainActor in self?.tick() }
        }
        RunLoop.main.add(t, forMode: .common)
        timer = t
    }

    private func tick() {
        let now = ProcessInfo.processInfo.systemUptime
        let p = NSEvent.mouseLocation
        samples.append((now, p.x))
        samples.removeAll { now - $0.t > 0.15 }

        guard let scr = NSScreen.screens.first(where: { NSMouseInRect(p, $0.frame, false) }) else { return }
        let f = scr.frame
        let side = p.x <= f.minX + 1 ? -1 : (p.x >= f.maxX - 2 ? 1 : 0)
        let atEdge = side != 0
        defer { wasAtEdge = atEdge }

        guard atEdge, !wasAtEdge, now - lastFire > 0.7, NSEvent.pressedMouseButtons == 0 else { return }
        guard let old = samples.first(where: { now - $0.t <= 0.09 }) else { return }
        let dt = max(0.001, now - old.t)
        let travel = (p.x - old.x) * CGFloat(side)
        let speed = travel / CGFloat(dt)
        let s = max(0, min(2, Settings.shared.sensitivity))
        let speedK: [CGFloat] = [2.4, 1.6, 1.05], travelK: [CGFloat] = [0.16, 0.10, 0.06]
        if speed > f.width * speedK[s] && travel > f.width * travelK[s] {
            lastFire = now
            onSwipe?(side, scr)
        }
    }
}

// MARK: - Global hotkey ⌃⌥Space (Carbon; no Accessibility permission needed)

enum HotKey {
    nonisolated(unsafe) static var handler: (() -> Void)?
    nonisolated(unsafe) private static var ref: EventHotKeyRef?

    static func register() {
        var spec = EventTypeSpec(eventClass: OSType(kEventClassKeyboard), eventKind: UInt32(kEventHotKeyPressed))
        InstallEventHandler(GetApplicationEventTarget(), { _, _, _ in
            DispatchQueue.main.async { HotKey.handler?() }
            return noErr
        }, 1, &spec, nil, nil)
        let id = EventHotKeyID(signature: OSType(0x474C5353), id: 1) // 'GLSS'
        RegisterEventHotKey(UInt32(kVK_Space), UInt32(controlKey | optionKey), id, GetApplicationEventTarget(), 0, &ref)
    }
}
