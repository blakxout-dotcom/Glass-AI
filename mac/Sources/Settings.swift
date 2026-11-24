import Foundation
import Security
import SwiftUI

// MARK: - Keychain (API key lives here, never in plain files)

enum Keychain {
    static let service = "com.glass.sidebar"

    static func read(_ account: String) -> String {
        let q: [String: Any] = [kSecClass as String: kSecClassGenericPassword,
                                kSecAttrService as String: service,
                                kSecAttrAccount as String: account,
                                kSecReturnData as String: true,
                                kSecMatchLimit as String: kSecMatchLimitOne]
        var out: AnyObject?
        guard SecItemCopyMatching(q as CFDictionary, &out) == errSecSuccess,
              let data = out as? Data, let s = String(data: data, encoding: .utf8) else { return "" }
        return s
    }

    static func write(_ account: String, _ value: String) {
        let base: [String: Any] = [kSecClass as String: kSecClassGenericPassword,
                                   kSecAttrService as String: service,
                                   kSecAttrAccount as String: account]
        SecItemDelete(base as CFDictionary)
        guard !value.isEmpty else { return }
        var add = base
        add[kSecValueData as String] = Data(value.utf8)
        add[kSecAttrAccessible as String] = kSecAttrAccessibleAfterFirstUnlock
        SecItemAdd(add as CFDictionary, nil)
    }
}

// MARK: - Settings

@MainActor
final class Settings: ObservableObject {
    static let shared = Settings()
    private let d = UserDefaults.standard

    @Published var provider: String { didSet { d.set(provider, forKey: "provider") } }
    @Published var model: String { didSet { d.set(model, forKey: "model") } }
    @Published var baseURL: String { didSet { d.set(baseURL, forKey: "baseURL") } }
    @Published var systemPrompt: String { didSet { d.set(systemPrompt, forKey: "systemPrompt") } }
    @Published var sensitivity: Int { didSet { d.set(sensitivity, forKey: "sensitivity") } }
    // glass
    @Published var tintHex: String { didSet { d.set(tintHex, forKey: "tintHex") } }
    @Published var tintStrength: Double { didSet { d.set(tintStrength, forKey: "tintStrength") } }
    @Published var glassStyle: String { didSet { d.set(glassStyle, forKey: "glassStyle") } } // clear | regular | frosted

    @Published var apiKey: String { didSet { Keychain.write("apiKey", apiKey) } }

    static let defaultTint = "#5C5E64"   // semi-gray
    static let defaultStrength = 0.42

    private init() {
        provider = d.string(forKey: "provider") ?? "anthropic"
        model = d.string(forKey: "model") ?? "claude-sonnet-5-5"
        baseURL = d.string(forKey: "baseURL") ?? "https://api.openai.com/v1"
        systemPrompt = d.string(forKey: "systemPrompt") ?? "You are a fast, concise assistant living in a sidebar. Keep answers tight."
        sensitivity = d.object(forKey: "sensitivity") as? Int ?? 1
        tintHex = d.string(forKey: "tintHex") ?? Settings.defaultTint
        tintStrength = d.object(forKey: "tintStrength") as? Double ?? Settings.defaultStrength
        glassStyle = d.string(forKey: "glassStyle") ?? "regular"
        apiKey = Keychain.read("apiKey")
    }

    var tintColor: Color { Color(hex: tintHex) ?? Color(hex: Settings.defaultTint)! }

    var providerLabel: String { Settings.label(provider) }

    static func label(_ p: String) -> String {
        switch p {
        case "anthropic": return "Claude"
        case "gemini": return "Gemini"
        case "groq": return "Groq"
        default: return "Assistant"
        }
    }

    // Pull a key out of pasted text or a .txt / .env / .json file.
    static func extractKey(_ text: String) -> String? {
        let patterns = [#"sk-ant-[A-Za-z0-9_\-]{20,}"#,
                        #"AIza[0-9A-Za-z_\-]{30,}"#,
                        #"gsk_[0-9A-Za-z]{20,}"#,
                        #"(?:ANTHROPIC|OPENAI|OPENROUTER|GROQ|GEMINI|GOOGLE)?_?API_KEY\s*[=:]\s*["']?([A-Za-z0-9_\-\.]{16,})"#,
                        #"sk-[A-Za-z0-9_\-]{20,}"#]
        for (i, p) in patterns.enumerated() {
            guard let re = try? NSRegularExpression(pattern: p),
                  let m = re.firstMatch(in: text, range: NSRange(text.startIndex..., in: text)) else { continue }
            let r = (i == 3 && m.numberOfRanges > 1) ? m.range(at: 1) : m.range
            if let rr = Range(r, in: text) { return String(text[rr]) }
        }
        let t = text.trimmingCharacters(in: .whitespacesAndNewlines)
        return (t.count >= 16 && !t.contains(" ") && !t.contains("\n")) ? t : nil
    }

    static func providerFor(key: String) -> String? {
        if key.hasPrefix("sk-ant-") { return "anthropic" }
        if key.hasPrefix("AIza") { return "gemini" }
        if key.hasPrefix("gsk_") { return "groq" }
        if key.hasPrefix("sk-") { return "openai" }
        return nil
    }
}

extension Color {
    init?(hex: String) {
        var h = hex.trimmingCharacters(in: .whitespaces).replacingOccurrences(of: "#", with: "")
        if h.count == 3 { h = h.map { "\($0)\($0)" }.joined() }
        guard h.count == 6, let v = Int(h, radix: 16) else { return nil }
        self.init(red: Double((v >> 16) & 255) / 255, green: Double((v >> 8) & 255) / 255, blue: Double(v & 255) / 255)
    }
}
