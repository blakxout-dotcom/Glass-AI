import Foundation
import SwiftUI

struct ChatMessage: Codable, Identifiable, Equatable {
    var id = UUID()
    var role: String          // "user" | "assistant"
    var content: String
    var provider: String = ""
    var model: String = ""
    var error: String? = nil
}

struct ChatSession: Codable, Identifiable, Equatable {
    var id = UUID()
    var title = "New chat"
    var updated = Date()
    var provider = ""
    var messages: [ChatMessage] = []
}

// Chats are JSON files in ~/Library/Application Support/Glass/chats, one per conversation.
@MainActor
final class ChatModel: ObservableObject {
    static let shared = ChatModel()

    @Published var chats: [ChatSession] = []
    @Published var current = ChatSession()
    @Published var streaming = false
    @Published var search = ""

    private var task: Task<Void, Never>?
    private let settings = Settings.shared

    private var dir: URL {
        let base = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
        return base.appendingPathComponent("Glass/chats", isDirectory: true)
    }

    private init() { load() }

    var filteredChats: [ChatSession] {
        let q = search.trimmingCharacters(in: .whitespaces)
        guard !q.isEmpty else { return chats }
        return chats.filter { c in
            c.title.localizedCaseInsensitiveContains(q) || c.messages.contains { $0.content.localizedCaseInsensitiveContains(q) }
        }
    }

    // MARK: persistence

    func load() {
        let fm = FileManager.default
        guard let files = try? fm.contentsOfDirectory(at: dir, includingPropertiesForKeys: nil) else { return }
        let dec = JSONDecoder()
        chats = files.filter { $0.pathExtension == "json" }
            .compactMap { try? dec.decode(ChatSession.self, from: Data(contentsOf: $0)) }
            .filter { !$0.messages.isEmpty }
            .sorted { $0.updated > $1.updated }
    }

    private func persist() {
        guard !current.messages.isEmpty else { return }
        if current.title == "New chat", let first = current.messages.first(where: { $0.role == "user" }) {
            current.title = ChatModel.makeTitle(first.content)
        }
        current.updated = Date()
        current.provider = settings.provider
        try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        if let data = try? JSONEncoder().encode(current) {
            try? data.write(to: dir.appendingPathComponent(current.id.uuidString + ".json"), options: .atomic)
        }
        chats.removeAll { $0.id == current.id }
        chats.insert(current, at: 0)
    }

    func delete(_ c: ChatSession) {
        try? FileManager.default.removeItem(at: dir.appendingPathComponent(c.id.uuidString + ".json"))
        chats.removeAll { $0.id == c.id }
        if c.id == current.id { newChat() }
    }

    func open(_ c: ChatSession) {
        stop()
        current = c
    }

    func newChat() {
        stop()
        current = ChatSession()
    }

    static func makeTitle(_ s: String) -> String {
        let t = s.split(whereSeparator: { $0.isWhitespace }).joined(separator: " ")
        if t.isEmpty { return "New chat" }
        return t.count > 42 ? String(t.prefix(40)).trimmingCharacters(in: .whitespaces) + "…" : t
    }

    static func relative(_ d: Date) -> String {
        let s = Date().timeIntervalSince(d)
        if s < 60 { return "Just now" }
        if s < 3600 { return "\(Int(s / 60))m ago" }
        if s < 86400 { return "\(Int(s / 3600))h ago" }
        if s < 172800 { return "Yesterday" }
        if s < 604800 { return "\(Int(s / 86400))d ago" }
        let f = DateFormatter(); f.dateFormat = "MMM d"
        return f.string(from: d)
    }

    // MARK: conversation

    func send(_ text: String) {
        let t = text.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !t.isEmpty, !streaming else { return }
        current.messages.append(ChatMessage(role: "user", content: t))
        persist()
        runReply()
    }

    func retry() {
        guard !streaming else { return }
        if current.messages.last?.role == "assistant" { current.messages.removeLast() }
        guard current.messages.last?.role == "user" else { return }
        runReply()
    }

    func stop() {
        task?.cancel()
        task = nil
        streaming = false
    }

    private func runReply() {
        let req = AIRequest(provider: settings.provider, model: settings.model, baseURL: settings.baseURL,
                            key: settings.apiKey, system: settings.systemPrompt)
        let history = current.messages
        let sessionID = current.id
        current.messages.append(ChatMessage(role: "assistant", content: "", provider: req.provider, model: req.model))
        streaming = true

        task = Task { [weak self] in
            var errorText: String? = nil
            do {
                for try await delta in AI.stream(req, history: history) {
                    guard let self, self.current.id == sessionID, !self.current.messages.isEmpty else { break }
                    self.current.messages[self.current.messages.count - 1].content += delta
                }
            } catch is CancellationError {
            } catch {
                errorText = error.localizedDescription
            }
            guard let self, self.current.id == sessionID, !self.current.messages.isEmpty else { return }
            let i = self.current.messages.count - 1
            if Task.isCancelled && self.current.messages[i].content.isEmpty { self.current.messages[i].content = "*Stopped.*" }
            self.current.messages[i].error = errorText
            if self.current.messages[i].content.isEmpty && errorText == nil { self.current.messages[i].content = "*(no response)*" }
            self.streaming = false
            self.task = nil
            self.persist()
        }
    }
}
