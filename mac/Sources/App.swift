import AppKit
import SwiftUI

@main
struct GlassMain {
    @MainActor static func main() {
        let app = NSApplication.shared
        let delegate = AppDelegate()
        app.delegate = delegate
        app.setActivationPolicy(.accessory)   // menu bar app: no Dock icon
        app.run()
    }
}

@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate {
    private var statusItem: NSStatusItem!
    private let swipe = SwipeDetector()

    func applicationDidFinishLaunching(_ notification: Notification) {
        // single instance
        let me = Bundle.main.bundleIdentifier ?? "com.glass.sidebar"
        if NSRunningApplication.runningApplications(withBundleIdentifier: me).count > 1 { NSApp.terminate(nil); return }

        PanelController.shared.setup()

        swipe.onSwipe = { side, screen in PanelController.shared.onSwipe(side: side, screen: screen) }
        swipe.start()

        HotKey.handler = { Task { @MainActor in PanelController.shared.toggle() } }
        HotKey.register()

        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
        if let b = statusItem.button {
            b.image = NSImage(systemSymbolName: "sparkles", accessibilityDescription: "Glass")
            b.image?.isTemplate = true
        }
        let menu = NSMenu()
        menu.addItem(withTitle: "Open Glass  (⌃⌥Space)", action: #selector(openPanel), keyEquivalent: "").target = self
        menu.addItem(withTitle: "Settings…", action: #selector(openSettings), keyEquivalent: ",").target = self
        menu.addItem(.separator())
        menu.addItem(withTitle: "Quit Glass", action: #selector(quit), keyEquivalent: "q").target = self
        statusItem.menu = menu

        // first launch: show it so people know it's there
        if Settings.shared.apiKey.isEmpty {
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.4) {
                PanelController.shared.show(side: 1, on: PanelController.shared.screenUnderMouse())
            }
        }
    }

    @objc func openPanel() {
        let pc = PanelController.shared
        if !pc.isShown { pc.show(side: 1, on: pc.screenUnderMouse()) }
    }

    @objc func openSettings() {
        openPanel()
        AppState.shared.showSettings = true
    }

    @objc func quit() { NSApp.terminate(nil) }
}
