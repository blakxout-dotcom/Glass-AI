import AppKit
import SwiftUI

struct RootView: View {
    @EnvironmentObject var s: Settings
    @EnvironmentObject var chat: ChatModel
    @EnvironmentObject var app: AppState

    var body: some View {
        ZStack {
            GlassSurface(radius: 30)
            HStack(spacing: 0) {
                if app.fullscreen {
                    ChatsSidebar()
                        .frame(width: 280)
                        .background(Color.white.opacity(0.04))
                        .overlay(Rectangle().fill(.white.opacity(0.1)).frame(width: 1), alignment: .trailing)
                        .transition(.move(edge: .leading).combined(with: .opacity))
                }
                VStack(spacing: 0) {
                    Header()
                    Rectangle().fill(.white.opacity(0.1)).frame(height: 1)
                    if app.showSettings {
                        SettingsView().transition(.opacity.combined(with: .move(edge: .trailing)))
                    } else {
                        VStack(spacing: 0) {
                            ConversationView()
                            Composer().padding(.horizontal, 14).padding(.bottom, 14).padding(.top, 4)
                        }
                        .frame(maxWidth: app.fullscreen ? 860 : .infinity)
                        .frame(maxWidth: .infinity)
                        .transition(.opacity)
                    }
                }
            }
            .clipShape(RoundedRectangle(cornerRadius: 30, style: .continuous))
        }
        .foregroundStyle(.white)
        .environment(\.colorScheme, .dark)
        .animation(.spring(response: 0.42, dampingFraction: 0.86), value: app.fullscreen)
        .animation(.easeOut(duration: 0.2), value: app.showSettings)
        .background(
            // ⌘N new chat, works anywhere in the panel
            Button("") { chat.newChat(); app.showSettings = false; app.focusTick += 1 }
                .keyboardShortcut("n", modifiers: .command).opacity(0)
        )
    }
}

// MARK: - Header

struct Header: View {
    @EnvironmentObject var s: Settings
    @EnvironmentObject var chat: ChatModel
    @EnvironmentObject var app: AppState

    var body: some View {
        HStack(spacing: 10) {
            if !app.fullscreen { Orb(size: 22) }
            VStack(alignment: .leading, spacing: 1) {
                Text(chat.current.messages.isEmpty ? "Glass" : chat.current.title)
                    .font(.system(size: 14.5, weight: .semibold)).lineLimit(1)
                Text(s.apiKey.isEmpty ? "Not connected" : "\(s.providerLabel) · \(s.model)")
                    .font(.system(size: 11)).foregroundStyle(.white.opacity(0.6)).lineLimit(1)
            }
            Spacer(minLength: 8)
            IconButton(symbol: "square.and.pencil", help: "New chat (⌘N)") { chat.newChat(); app.showSettings = false; app.focusTick += 1 }
            IconButton(symbol: app.pinned ? "pin.fill" : "pin", help: app.pinned ? "Unpin" : "Pin (stay open)") { app.pinned.toggle() }
            IconButton(symbol: "gearshape", help: "Settings") { app.showSettings.toggle() }
            IconButton(symbol: app.fullscreen ? "arrow.down.right.and.arrow.up.left" : "arrow.up.left.and.arrow.down.right",
                       help: app.fullscreen ? "Exit fullscreen" : "Fullscreen") { PanelController.shared.toggleFullscreen() }
            IconButton(symbol: "xmark", help: "Close (Esc)") { PanelController.shared.hide() }
        }
        .padding(.horizontal, 16).padding(.vertical, 12)
    }
}

// MARK: - Conversation

struct ConversationView: View {
    @EnvironmentObject var chat: ChatModel
    @EnvironmentObject var app: AppState

    var body: some View {
        if chat.current.messages.isEmpty {
            Welcome()
        } else {
            ScrollViewReader { proxy in
                ScrollView(showsIndicators: false) {
                    LazyVStack(alignment: .leading, spacing: 14) {
                        ForEach(chat.current.messages) { m in
                            MessageRow(message: m, isLast: m.id == chat.current.messages.last?.id)
                                .id(m.id)
                                .transition(.asymmetric(insertion: .opacity.combined(with: .offset(y: 10)), removal: .opacity))
                        }
                        Color.clear.frame(height: 1).id("bottom")
                    }
                    .padding(.horizontal, 18).padding(.vertical, 16)
                    .animation(.easeOut(duration: 0.25), value: chat.current.messages.count)
                }
                .onAppear { proxy.scrollTo("bottom", anchor: .bottom) }
                .onChange(of: chat.current.messages.last?.content) { _ in proxy.scrollTo("bottom", anchor: .bottom) }
                .onChange(of: chat.current.id) { _ in proxy.scrollTo("bottom", anchor: .bottom) }
            }
        }
    }
}

struct Welcome: View {
    @EnvironmentObject var s: Settings
    @EnvironmentObject var app: AppState
    @EnvironmentObject var chat: ChatModel

    private var greeting: String {
        let h = Calendar.current.component(.hour, from: Date())
        return h < 5 ? "Up late?" : h < 12 ? "Good morning" : h < 17 ? "Good afternoon" : "Good evening"
    }

    private let ideas: [(String, String, String)] = [
        ("text.alignleft", "Summarize something", "Summarize this in a few bullet points:\n"),
        ("lightbulb", "Explain a concept", "Explain this simply, like I'm new to it: "),
        ("envelope", "Draft a message", "Write a short, friendly message that "),
        ("ladybug", "Debug some code", "Help me find the bug in this code:\n"),
    ]

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            Spacer()
            Orb(size: 46, glow: true).padding(.bottom, 18)
            Text(greeting).font(.system(size: 27, weight: .semibold))
            Text("How can I help?").font(.system(size: 22)).foregroundStyle(.white.opacity(0.7)).padding(.bottom, 22)
            FlowLayout(spacing: 8) {
                ForEach(ideas, id: \.1) { idea in
                    Pill(action: { Composer.prefill = idea.2; app.focusTick += 1 }) {
                        HStack(spacing: 8) { Image(systemName: idea.0).opacity(0.85); Text(idea.1) }
                    }
                }
            }
            Text(s.apiKey.isEmpty ? "Add an API key in Settings (the gear above) to get started."
                                  : "Tip: flick your cursor into a screen edge any time to summon me.")
                .font(.system(size: 11.5)).foregroundStyle(.white.opacity(0.5)).padding(.top, 16)
            Spacer()
        }
        .padding(.horizontal, 22)
        .frame(maxWidth: .infinity, alignment: .leading)
    }
}

struct MessageRow: View {
    @EnvironmentObject var chat: ChatModel
    var message: ChatMessage
    var isLast: Bool
    @State private var copied = false

    var body: some View {
        if message.role == "user" {
            HStack {
                Spacer(minLength: 44)
                Text(message.content)
                    .font(.system(size: 14))
                    .textSelection(.enabled)
                    .padding(.horizontal, 15).padding(.vertical, 10)
                    .background(UnevenRoundedRectangle(topLeadingRadius: 20, bottomLeadingRadius: 20, bottomTrailingRadius: 6, topTrailingRadius: 20, style: .continuous)
                        .fill(.white.opacity(0.2)))
                    .overlay(UnevenRoundedRectangle(topLeadingRadius: 20, bottomLeadingRadius: 20, bottomTrailingRadius: 6, topTrailingRadius: 20, style: .continuous)
                        .strokeBorder(.white.opacity(0.28)))
            }
        } else {
            VStack(alignment: .leading, spacing: 6) {
                HStack(spacing: 8) {
                    Orb(size: 20)
                    Text(Settings.label(message.provider)).font(.system(size: 13, weight: .semibold))
                    Text(message.model).font(.system(size: 11.5)).foregroundStyle(.white.opacity(0.55)).lineLimit(1)
                }
                Group {
                    if message.content.isEmpty && chat.streaming && isLast {
                        TypingDots()
                    } else {
                        MarkdownView(text: message.content).font(.system(size: 14)).lineSpacing(3)
                    }
                    if let e = message.error {
                        Label(e, systemImage: "exclamationmark.triangle.fill")
                            .font(.system(size: 12.5)).foregroundStyle(Color(red: 1, green: 0.72, blue: 0.72))
                            .textSelection(.enabled)
                    }
                }
                .padding(.leading, 28)

                if !(chat.streaming && isLast) {
                    HStack(spacing: 2) {
                        if !message.content.isEmpty {
                            ActionButton(symbol: copied ? "checkmark" : "doc.on.doc", help: "Copy") {
                                NSPasteboard.general.clearContents()
                                NSPasteboard.general.setString(message.content, forType: .string)
                                copied = true
                                DispatchQueue.main.asyncAfter(deadline: .now() + 1.2) { copied = false }
                            }
                        }
                        if isLast { ActionButton(symbol: "arrow.clockwise", help: "Retry") { chat.retry() } }
                    }
                    .padding(.leading, 22)
                }
            }
        }
    }
}

struct ActionButton: View {
    var symbol: String
    var help: String
    var action: () -> Void
    @State private var hover = false
    var body: some View {
        Button(action: action) {
            Image(systemName: symbol).font(.system(size: 11.5))
                .frame(width: 26, height: 24)
                .background(RoundedRectangle(cornerRadius: 7).fill(.white.opacity(hover ? 0.14 : 0)))
                .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .foregroundStyle(.white.opacity(hover ? 1 : 0.6))
        .onHover { hover = $0 }
        .help(help)
    }
}

struct TypingDots: View {
    @State private var on = false
    var body: some View {
        HStack(spacing: 5) {
            ForEach(0..<3) { i in
                Circle().fill(.white).frame(width: 7, height: 7)
                    .opacity(on ? 1 : 0.25)
                    .animation(.easeInOut(duration: 0.42).repeatForever().delay(Double(i) * 0.16), value: on)
            }
        }
        .padding(.vertical, 6)
        .onAppear { on = true }
    }
}

// MARK: - Composer

struct Composer: View {
    @EnvironmentObject var s: Settings
    @EnvironmentObject var chat: ChatModel
    @EnvironmentObject var app: AppState
    @State private var text = ""
    @FocusState private var focused: Bool

    // suggestion chips drop text here
    static var prefill: String? = nil

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            TextField("Ask anything…", text: $text, axis: .vertical)
                .textFieldStyle(.plain)
                .font(.system(size: 14))
                .lineLimit(1...8)
                .focused($focused)
                .onSubmit(send)
                .padding(.horizontal, 4).padding(.top, 2)
            HStack {
                Pill(action: { app.showSettings = true }) {
                    HStack(spacing: 7) {
                        Circle().fill(orbGradient).frame(width: 7, height: 7)
                        Text(s.apiKey.isEmpty ? "Add API key" : s.model).lineLimit(1).frame(maxWidth: 190, alignment: .leading)
                        Image(systemName: "chevron.down").font(.system(size: 8, weight: .semibold)).opacity(0.7)
                    }
                    .font(.system(size: 11.5))
                }
                Spacer()
                Button(action: { chat.streaming ? chat.stop() : send() }) {
                    Image(systemName: chat.streaming ? "stop.fill" : "arrow.up")
                        .font(.system(size: 13, weight: .bold))
                        .foregroundStyle(Color(white: 0.07))
                        .frame(width: 32, height: 32)
                        .background(Circle().fill(.white.opacity(canSend || chat.streaming ? 0.95 : 0.3)))
                }
                .buttonStyle(PressStyle())
                .disabled(!canSend && !chat.streaming)
                .help(chat.streaming ? "Stop" : "Send (Return)")
            }
        }
        .padding(.leading, 12).padding(.trailing, 8).padding(.vertical, 10)
        .background(RoundedRectangle(cornerRadius: 22, style: .continuous).fill(.white.opacity(0.13)))
        .overlay(RoundedRectangle(cornerRadius: 22, style: .continuous).strokeBorder(.white.opacity(focused ? 0.4 : 0.22)))
        .onChange(of: app.focusTick) { _ in
            if let p = Composer.prefill { text = p; Composer.prefill = nil }
            DispatchQueue.main.async { focused = true }
        }
        .onAppear { DispatchQueue.main.async { focused = true } }
    }

    private var canSend: Bool { !text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty }

    private func send() {
        guard canSend, !chat.streaming else { return }
        if s.apiKey.isEmpty { app.showSettings = true; return }
        chat.send(text)
        text = ""
    }
}

// MARK: - Chats sidebar (fullscreen)

struct ChatsSidebar: View {
    @EnvironmentObject var chat: ChatModel
    @EnvironmentObject var app: AppState

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            HStack(spacing: 10) {
                Orb(size: 26)
                Text("Glass").font(.system(size: 17, weight: .semibold))
            }
            .padding(.leading, 6).padding(.bottom, 6)

            Button { chat.newChat(); app.showSettings = false; app.focusTick += 1 } label: {
                HStack(spacing: 10) {
                    Image(systemName: "plus").font(.system(size: 12, weight: .semibold))
                    Text("New chat").font(.system(size: 13.5, weight: .semibold))
                    Spacer()
                    Text("⌘N").font(.system(size: 11)).foregroundStyle(.white.opacity(0.5))
                }
                .padding(.horizontal, 14).padding(.vertical, 10)
                .background(RoundedRectangle(cornerRadius: 14, style: .continuous).fill(.white.opacity(0.16)))
                .overlay(RoundedRectangle(cornerRadius: 14, style: .continuous).strokeBorder(.white.opacity(0.22)))
                .contentShape(Rectangle())
            }
            .buttonStyle(PressStyle())

            HStack(spacing: 8) {
                Image(systemName: "magnifyingglass").font(.system(size: 12)).foregroundStyle(.white.opacity(0.6))
                TextField("Search chats", text: $chat.search).textFieldStyle(.plain).font(.system(size: 13))
            }
            .padding(.horizontal, 12).padding(.vertical, 8)
            .background(RoundedRectangle(cornerRadius: 12, style: .continuous).fill(.white.opacity(0.08)))
            .overlay(RoundedRectangle(cornerRadius: 12, style: .continuous).strokeBorder(.white.opacity(0.15)))

            Text("Recent").font(.system(size: 11.5)).foregroundStyle(.white.opacity(0.5)).padding(.leading, 8).padding(.top, 6)

            ScrollView(showsIndicators: false) {
                LazyVStack(spacing: 2) {
                    if chat.filteredChats.isEmpty {
                        Text(chat.search.isEmpty ? "Your chats will show up here." : "No chats match.")
                            .font(.system(size: 12)).foregroundStyle(.white.opacity(0.5))
                            .frame(maxWidth: .infinity, alignment: .leading).padding(.leading, 10).padding(.top, 4)
                    }
                    ForEach(chat.filteredChats) { c in ChatRow(chat: c, active: c.id == chat.current.id) }
                }
            }
        }
        .padding(.horizontal, 12).padding(.top, 18).padding(.bottom, 14)
    }
}

struct ChatRow: View {
    @EnvironmentObject var model: ChatModel
    @EnvironmentObject var app: AppState
    var chat: ChatSession
    var active: Bool
    @State private var hover = false

    var body: some View {
        HStack(spacing: 6) {
            VStack(alignment: .leading, spacing: 2) {
                Text(chat.title).font(.system(size: 13)).lineLimit(1)
                Text("\(ChatModel.relative(chat.updated)) · \(Settings.label(chat.provider))")
                    .font(.system(size: 11)).foregroundStyle(.white.opacity(0.55))
            }
            Spacer(minLength: 0)
            if hover {
                ActionButton(symbol: "trash", help: "Delete chat") { withAnimation { model.delete(chat) } }
            }
        }
        .padding(.horizontal, 12).padding(.vertical, 8)
        .background(RoundedRectangle(cornerRadius: 12, style: .continuous)
            .fill(.white.opacity(active ? 0.18 : (hover ? 0.1 : 0))))
        .contentShape(Rectangle())
        .onTapGesture { model.open(chat); app.showSettings = false; app.focusTick += 1 }
        .onHover { hover = $0 }
    }
}
