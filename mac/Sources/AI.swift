import Foundation

struct AIRequest {
    var provider: String
    var model: String
    var baseURL: String
    var key: String
    var system: String
}

enum AIError: LocalizedError {
    case message(String)
    var errorDescription: String? { if case .message(let m) = self { return m }; return nil }
}

enum AI {
    static let geminiBase = "https://generativelanguage.googleapis.com/v1beta/openai"
    static let groqBase = "https://api.groq.com/openai/v1"
    static let geminiDefault = "gemini-4-hydrogen"

    // Streams text deltas for any provider.
    static func stream(_ r: AIRequest, history: [ChatMessage]) -> AsyncThrowingStream<String, Error> {
        AsyncThrowingStream { continuation in
            let task = Task {
                do {
                    guard !r.key.isEmpty else { throw AIError.message("No API key yet. Open Settings to add one.") }
                    let anthropic = r.provider == "anthropic"
                    var msgs: [[String: Any]] = []
                    if !anthropic && !r.system.isEmpty { msgs.append(["role": "system", "content": r.system]) }
                    for m in history where !m.content.isEmpty { msgs.append(["role": m.role, "content": m.content]) }

                    var req: URLRequest
                    if anthropic {
                        req = URLRequest(url: URL(string: "https://api.anthropic.com/v1/messages")!)
                        req.setValue(r.key, forHTTPHeaderField: "x-api-key")
                        req.setValue("2023-06-01", forHTTPHeaderField: "anthropic-version")
                        var body: [String: Any] = ["model": r.model, "max_tokens": 4096, "stream": true, "messages": msgs]
                        if !r.system.isEmpty { body["system"] = r.system }
                        req.httpBody = try JSONSerialization.data(withJSONObject: body)
                    } else {
                        let base = r.provider == "gemini" ? geminiBase : r.provider == "groq" ? groqBase : r.baseURL
                        let trimmed = base.hasSuffix("/") ? String(base.dropLast()) : base
                        guard let url = URL(string: trimmed + "/chat/completions") else { throw AIError.message("Invalid base URL.") }
                        req = URLRequest(url: url)
                        req.setValue("Bearer \(r.key)", forHTTPHeaderField: "Authorization")
                        req.httpBody = try JSONSerialization.data(withJSONObject: ["model": r.model, "stream": true, "messages": msgs])
                    }
                    req.httpMethod = "POST"
                    req.setValue("application/json", forHTTPHeaderField: "Content-Type")
                    req.timeoutInterval = 300

                    let (bytes, resp) = try await URLSession.shared.bytes(for: req)
                    if let http = resp as? HTTPURLResponse, !(200..<300).contains(http.statusCode) {
                        var raw = ""
                        for try await line in bytes.lines { raw += line; if raw.count > 4000 { break } }
                        throw AIError.message("API error \(http.statusCode): \(errorText(raw))")
                    }
                    for try await line in bytes.lines {
                        try Task.checkCancellation()
                        guard line.hasPrefix("data:") else { continue }
                        let data = line.dropFirst(5).trimmingCharacters(in: .whitespaces)
                        if data == "[DONE]" { break }
                        guard let obj = try? JSONSerialization.jsonObject(with: Data(data.utf8)) as? [String: Any] else { continue }
                        if anthropic {
                            if obj["type"] as? String == "error" { throw AIError.message(errorText(data)) }
                            if obj["type"] as? String == "content_block_delta",
                               let d = obj["delta"] as? [String: Any], let t = d["text"] as? String { continuation.yield(t) }
                        } else if let choices = obj["choices"] as? [[String: Any]],
                                  let d = choices.first?["delta"] as? [String: Any], let t = d["content"] as? String {
                            continuation.yield(t)
                        }
                    }
                    continuation.finish()
                } catch {
                    continuation.finish(throwing: error)
                }
            }
            continuation.onTermination = { _ in task.cancel() }
        }
    }

    // Every Gemini chat model on this key, straight from Google.
    static func geminiModels(key: String) async throws -> [(id: String, name: String)] {
        var out: [(String, String)] = []
        var token: String? = nil
        repeat {
            var s = "https://generativelanguage.googleapis.com/v1beta/models?pageSize=1000"
            if let t = token { s += "&pageToken=" + (t.addingPercentEncoding(withAllowedCharacters: .urlQueryAllowed) ?? t) }
            var req = URLRequest(url: URL(string: s)!)
            req.setValue(key, forHTTPHeaderField: "x-goog-api-key")
            let (data, resp) = try await URLSession.shared.data(for: req)
            try check(resp, data)
            let obj = (try JSONSerialization.jsonObject(with: data) as? [String: Any]) ?? [:]
            for m in (obj["models"] as? [[String: Any]]) ?? [] {
                let id = ((m["name"] as? String) ?? "").replacingOccurrences(of: "models/", with: "")
                let methods = (m["supportedGenerationMethods"] as? [String]) ?? []
                guard id.hasPrefix("gemini"), methods.contains("generateContent") else { continue }
                out.append((id, (m["displayName"] as? String) ?? id))
            }
            token = obj["nextPageToken"] as? String
            if token == "" { token = nil }
        } while token != nil
        return out.sorted { $0.0 > $1.0 }.map { (id: $0.0, name: $0.1) }
    }

    // Every chat model on this Groq account (skips speech / TTS / guard models).
    static func groqModels(key: String) async throws -> [(id: String, name: String)] {
        var req = URLRequest(url: URL(string: groqBase + "/models")!)
        req.setValue("Bearer \(key)", forHTTPHeaderField: "Authorization")
        let (data, resp) = try await URLSession.shared.data(for: req)
        try check(resp, data)
        let obj = (try JSONSerialization.jsonObject(with: data) as? [String: Any]) ?? [:]
        let skip = ["whisper", "tts", "guard", "orpheus"]
        return ((obj["data"] as? [[String: Any]]) ?? [])
            .filter { ($0["active"] as? Bool) ?? true }
            .compactMap { $0["id"] as? String }
            .filter { id in !skip.contains { id.lowercased().contains($0) } }
            .sorted()
            .map { (id: $0, name: $0) }
    }

    private static func check(_ resp: URLResponse, _ data: Data) throws {
        if let http = resp as? HTTPURLResponse, !(200..<300).contains(http.statusCode) {
            throw AIError.message("Couldn't list models (\(http.statusCode)): " + errorText(String(decoding: data, as: UTF8.self)))
        }
    }

    static func errorText(_ raw: String) -> String {
        if let re = try? NSRegularExpression(pattern: #""message"\s*:\s*"((?:[^"\\]|\\.)*)""#),
           let m = re.firstMatch(in: raw, range: NSRange(raw.startIndex..., in: raw)),
           let r = Range(m.range(at: 1), in: raw) {
            return String(raw[r]).replacingOccurrences(of: "\\n", with: " ").replacingOccurrences(of: "\\\"", with: "\"")
        }
        return String(raw.prefix(300))
    }
}
