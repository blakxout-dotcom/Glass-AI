import AppKit
import SwiftUI

struct SettingsView: View {
    @EnvironmentObject var s: Settings
    @EnvironmentObject var app: AppState

    @State private var keyStatus = ""
    @State private var models: [(id: String, name: String)] = []
    @State private var modelsStatus = ""
    @State private var modelsFor = ""
    @State private var hexDraft = ""

    private let swatches: [(String, String)] = [
        ("Semi-gray", "#5C5E64"), ("Graphite", "#1E2026"), ("Smoke", "#8C8E94"),
        ("Midnight", "#162246"), ("Ocean", "#124E76"), ("Violet", "#482C7C"),
        ("Rose", "#842C52"), ("Forest", "#1C523C"), ("Amber", "#805216"),
    ]

    var body: some View {
        ScrollView(showsIndicators: false) {
            VStack(alignment: .leading, spacing: 12) {
                HStack {
                    Text("Settings").font(.system(size: 22, weight: .semibold))
                    Spacer()
                    Pill("Done") { app.showSettings = false; app.focusTick += 1 }
                }
                .padding(.bottom, 4)

                glassCard
                providerCard
                behaviorCard

                Text("Changes apply instantly. Your API key is stored in the macOS Keychain. ⌃⌥Space toggles the sidebar; ⌘N starts a new chat.")
                    .font(.system(size: 11)).foregroundStyle(.white.opacity(0.5)).padding(.horizontal, 4)
            }
            .padding(16)
            .frame(maxWidth: app.fullscreen ? 620 : .infinity)
            .frame(maxWidth: .infinity)
        }
        .onAppear {
            hexDraft = s.tintHex
            keyStatus = s.apiKey.isEmpty ? "No key saved yet." : "Key saved in Keychain."
            loadModelsIfNeeded()
        }
    }

    // MARK: cards

    private var glassCard: some View {
        Card(title: "Glass") {
            label("Style")
            Picker("", selection: $s.glassStyle) {
                Text("Clear").tag("clear")
                Text("Regular").tag("regular")
                Text("Frosted").tag("frosted")
            }
            .pickerStyle(.segmented).labelsHidden()

            label("Tint")
            FlowLayout(spacing: 6) {
                ForEach(swatches, id: \.1) { sw in
                    let selected = s.tintHex.uppercased() == sw.1
                    Button { s.tintHex = sw.1; hexDraft = sw.1 } label: {
                        Circle().fill(Color(hex: sw.1)!)
                            .frame(width: 28, height: 28)
                            .overlay(Circle().strokeBorder(.white.opacity(0.35)))
                            .padding(3)
                            .overlay(Circle().strokeBorder(.white, lineWidth: selected ? 2 : 0))
                    }
                    .buttonStyle(PressStyle()).help(sw.0)
                }
            }
            HStack(spacing: 8) {
                Circle().fill(s.tintColor).frame(width: 14, height: 14).overlay(Circle().strokeBorder(.white.opacity(0.5)))
                TextField("#5C5E64", text: $hexDraft)
                    .textFieldStyle(.plain).font(.system(size: 12.5, design: .monospaced)).frame(width: 80)
                    .onSubmit { if Color(hex: hexDraft) != nil { s.tintHex = "#" + hexDraft.replacingOccurrences(of: "#", with: "").uppercased() } else { hexDraft = s.tintHex } }
                Text("Custom hex — press Return").font(.system(size: 11)).foregroundStyle(.white.opacity(0.5))
            }
            .padding(.horizontal, 10).padding(.vertical, 7)
            .background(field)
            .padding(.top, 4)

            HStack {
                label("Tint strength")
                Spacer()
                Text("\(Int((s.tintStrength / 0.85 * 100).rounded()))%").font(.system(size: 11.5)).foregroundStyle(.white.opacity(0.8)).padding(.top, 10)
            }
            Slider(value: $s.tintStrength, in: 0...0.85).tint(.white)

            Pill("Reset to semi-gray") {
                s.tintHex = Settings.defaultTint; hexDraft = Settings.defaultTint
                s.tintStrength = Settings.defaultStrength; s.glassStyle = "regular"
            }
            .padding(.top, 8)
        }
    }

    private var providerCard: some View {
        Card(title: "AI provider") {
            label("API key")
            SecureField("Paste your key", text: $s.apiKey)
                .textFieldStyle(.plain).font(.system(size: 13.5))
                .padding(.horizontal, 12).padding(.vertical, 9).background(field)
                .onChange(of: s.apiKey) { k in
                    if let p = Settings.providerFor(key: k), p != s.provider { setProvider(p) }
                }
            HStack(spacing: 6) {
                Pill("Import from file…") { importKey() }
                Pill("Paste") {
                    if let t = NSPasteboard.general.string(forType: .string), let k = Settings.extractKey(t) { accept(k, "Pasted from clipboard.") }
                    else { keyStatus = "Clipboard doesn't contain an API key." }
                }
                Pill("Remove") { s.apiKey = ""; keyStatus = "Key removed." }
            }
            .padding(.top, 8)
            Text(keyStatus).font(.system(size: 11.5)).foregroundStyle(.white.opacity(0.7)).padding(.top, 6)

            label("Provider")
            FlowLayout(spacing: 6) {
                Pill("Anthropic (Claude)", selected: s.provider == "anthropic") { setProvider("anthropic") }
                Pill("Google Gemini", selected: s.provider == "gemini") { setProvider("gemini") }
                Pill("Groq", selected: s.provider == "groq") { setProvider("groq") }
                Pill("OpenAI-compatible", selected: s.provider == "openai") { setProvider("openai") }
            }

            if s.provider == "gemini" || s.provider == "groq" {
                HStack {
                    label(s.provider == "gemini" ? "Gemini models" : "Groq models")
                    Spacer()
                    Pill("Refresh") { modelsFor = ""; loadModelsIfNeeded() }.padding(.top, 8)
                }
                if !modelsStatus.isEmpty {
                    Text(modelsStatus).font(.system(size: 11.5)).foregroundStyle(.white.opacity(0.65)).padding(.bottom, 4)
                }
                FlowLayout(spacing: 6) {
                    ForEach(models, id: \.id) { m in
                        Pill(m.name, selected: s.model == m.id) { s.model = m.id }.help(m.id)
                    }
                }
            }

            label("Model")
            TextField("Model name", text: $s.model)
                .textFieldStyle(.plain).font(.system(size: 13.5))
                .padding(.horizontal, 12).padding(.vertical, 9).background(field)

            if s.provider == "openai" {
                label("Base URL (OpenAI, OpenRouter, local…)")
                TextField("https://api.openai.com/v1", text: $s.baseURL)
                    .textFieldStyle(.plain).font(.system(size: 13.5))
                    .padding(.horizontal, 12).padding(.vertical, 9).background(field)
            }
        }
    }

    private var behaviorCard: some View {
        Card(title: "Behavior") {
            label("Swipe sensitivity")
            Picker("", selection: $s.sensitivity) {
                Text("Hard flick").tag(0)
                Text("Normal").tag(1)
                Text("Light").tag(2)
            }
            .pickerStyle(.segmented).labelsHidden()

            label("System prompt")
            TextEditor(text: $s.systemPrompt)
                .font(.system(size: 13))
                .scrollContentBackground(.hidden)
                .frame(minHeight: 70, maxHeight: 140)
                .padding(8).background(field)
        }
    }

    // MARK: helpers

    private var field: some View {
        RoundedRectangle(cornerRadius: 12, style: .continuous).fill(.white.opacity(0.1))
            .overlay(RoundedRectangle(cornerRadius: 12, style: .continuous).strokeBorder(.white.opacity(0.16)))
    }

    private func label(_ t: String) -> some View {
        Text(t).font(.system(size: 11.5)).foregroundStyle(.white.opacity(0.65)).padding(.top, 12).padding(.leading, 2)
    }

    private func setProvider(_ p: String) {
        guard p != s.provider else { return }
        s.provider = p
        switch p {
        case "anthropic": s.model = "claude-sonnet-5-5"
        case "gemini": s.model = AI.geminiDefault
        default: s.model = ""
        }
        models = []; modelsFor = ""
        loadModelsIfNeeded()
    }

    private func accept(_ k: String, _ how: String) {
        s.apiKey = k
        if let p = Settings.providerFor(key: k) { setProvider(p) }
        keyStatus = how + " Looks like a \(s.providerLabel == "Assistant" ? "OpenAI-compatible" : s.providerLabel) key — saved."
        modelsFor = ""; loadModelsIfNeeded()
    }

    private func loadModelsIfNeeded() {
        let p = s.provider
        guard p == "gemini" || p == "groq", modelsFor != p else { return }
        guard !s.apiKey.isEmpty else { modelsStatus = "Add your key above, then hit Refresh to load every model."; return }
        modelsFor = p
        modelsStatus = "Loading models…"
        let key = s.apiKey
        Task {
            do {
                let list = p == "gemini" ? try await AI.geminiModels(key: key) : try await AI.groqModels(key: key)
                guard s.provider == p else { return }
                models = list
                modelsStatus = "\(list.count) models available. Tap one to use it."
                if !list.contains(where: { $0.id == s.model }), (p == "groq" || s.model.isEmpty), let first = list.first { s.model = first.id }
            } catch {
                modelsStatus = error.localizedDescription
                modelsFor = ""
            }
        }
    }

    private func importKey() {
        let pc = PanelController.shared
        pc.suppressAutoHide = true
        defer { pc.suppressAutoHide = false }
        let open = NSOpenPanel()
        open.title = "Import API key"
        open.allowsMultipleSelection = false
        open.canChooseDirectories = false
        open.level = .modalPanel
        NSApp.activate(ignoringOtherApps: true)
        if open.runModal() == .OK, let url = open.url {
            if let text = try? String(contentsOf: url, encoding: .utf8), let k = Settings.extractKey(text) {
                accept(k, "Imported from \(url.lastPathComponent).")
            } else {
                keyStatus = "Couldn't find an API key in that file."
            }
        }
    }
}

struct Card<Content: View>: View {
    var title: String
    @ViewBuilder var content: () -> Content
    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            Text(title).font(.system(size: 14, weight: .semibold))
            content()
        }
        .padding(16)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(RoundedRectangle(cornerRadius: 18, style: .continuous).fill(.white.opacity(0.07)))
        .overlay(RoundedRectangle(cornerRadius: 18, style: .continuous).strokeBorder(.white.opacity(0.13)))
    }
}
