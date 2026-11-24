import AppKit
import SwiftUI

// MARK: - The glass

/// Real Liquid Glass on macOS 26+ (live refraction of whatever is behind the window),
/// tinted with your color. Falls back to a tinted behind-window blur on older macOS.
struct GlassSurface: View {
    @EnvironmentObject var s: Settings
    var radius: CGFloat

    var body: some View {
        let shape = RoundedRectangle(cornerRadius: radius, style: .continuous)
        let tint = s.tintColor.opacity(s.tintStrength)
        ZStack {
            if s.glassStyle == "frosted" {
                BehindWindowBlur(material: .hudWindow).clipShape(shape)
            }
            liquid(shape, tint)
            // crisp rim light, brighter top-left like real glass catching light
            shape.strokeBorder(
                LinearGradient(colors: [.white.opacity(0.6), .white.opacity(0.08), .white.opacity(0.04), .white.opacity(0.35)],
                               startPoint: .topLeading, endPoint: .bottomTrailing),
                lineWidth: 1)
            // soft gloss toward the top
            shape.fill(LinearGradient(colors: [.white.opacity(0.07), .clear], startPoint: .top, endPoint: .center))
                .allowsHitTesting(false)
        }
    }

    @ViewBuilder
    private func liquid(_ shape: RoundedRectangle, _ tint: Color) -> some View {
        #if compiler(>=6.2)
        if #available(macOS 26.0, *) {
            Color.clear.glassEffect(s.glassStyle == "clear" ? Glass.clear.tint(tint) : Glass.regular.tint(tint), in: shape)
        } else {
            fallback(shape, tint)
        }
        #else
        fallback(shape, tint)
        #endif
    }

    private func fallback(_ shape: RoundedRectangle, _ tint: Color) -> some View {
        BehindWindowBlur(material: s.glassStyle == "clear" ? .underWindowBackground : .hudWindow)
            .clipShape(shape)
            .overlay(shape.fill(tint))
    }
}

struct BehindWindowBlur: NSViewRepresentable {
    var material: NSVisualEffectView.Material
    func makeNSView(context: Context) -> NSVisualEffectView {
        let v = NSVisualEffectView()
        v.blendingMode = .behindWindow
        v.state = .active
        v.material = material
        return v
    }
    func updateNSView(_ v: NSVisualEffectView, context: Context) { v.material = material }
}

// MARK: - Shared bits

let orbGradient = LinearGradient(colors: [Color(red: 0.60, green: 0.90, blue: 1.0),
                                          Color(red: 0.71, green: 0.61, blue: 1.0),
                                          Color(red: 1.0, green: 0.66, blue: 0.85)],
                                 startPoint: .topLeading, endPoint: .bottomTrailing)

struct Orb: View {
    var size: CGFloat
    var glow = false
    var body: some View {
        Circle().fill(orbGradient).frame(width: size, height: size)
            .overlay(Circle().fill(LinearGradient(colors: [.white.opacity(0.55), .clear], startPoint: .top, endPoint: .center))
                .padding(size * 0.12).blur(radius: size * 0.08))
            .shadow(color: glow ? Color(red: 0.71, green: 0.61, blue: 1.0).opacity(0.7) : .clear, radius: size * 0.5)
    }
}

/// Round icon button with hover highlight.
struct IconButton: View {
    var symbol: String
    var help: String
    var action: () -> Void
    @State private var hover = false
    var body: some View {
        Button(action: action) {
            Image(systemName: symbol)
                .font(.system(size: 13, weight: .medium))
                .frame(width: 30, height: 30)
                .background(Circle().fill(.white.opacity(hover ? 0.16 : 0)))
                .overlay(Circle().strokeBorder(.white.opacity(hover ? 0.2 : 0), lineWidth: 1))
                .contentShape(Circle())
        }
        .buttonStyle(PressStyle())
        .foregroundStyle(.white.opacity(0.9))
        .onHover { hover = $0 }
        .help(help)
    }
}

struct PressStyle: ButtonStyle {
    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .scaleEffect(configuration.isPressed ? 0.92 : 1)
            .animation(.spring(response: 0.25, dampingFraction: 0.6), value: configuration.isPressed)
    }
}

/// Glass pill used for options, chips, suggestion buttons.
struct Pill<Label: View>: View {
    var selected = false
    var action: () -> Void
    @ViewBuilder var label: () -> Label
    @State private var hover = false
    var body: some View {
        Button(action: action) {
            label()
                .font(.system(size: 12.5))
                .padding(.horizontal, 13).padding(.vertical, 7)
                .background(Capsule().fill(.white.opacity(selected ? 0.30 : (hover ? 0.14 : 0.08))))
                .overlay(Capsule().strokeBorder(.white.opacity(selected ? 0.55 : (hover ? 0.35 : 0.18)), lineWidth: 1))
                .contentShape(Capsule())
        }
        .buttonStyle(PressStyle())
        .foregroundStyle(.white)
        .onHover { hover = $0 }
    }
}

extension Pill where Label == Text {
    init(_ title: String, selected: Bool = false, action: @escaping () -> Void) {
        self.selected = selected
        self.action = action
        self.label = { Text(title) }
    }
}

/// Wrapping row layout (chips, swatches, model lists).
struct FlowLayout: Layout {
    var spacing: CGFloat = 8
    func sizeThatFits(proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) -> CGSize {
        let maxW = proposal.width ?? .infinity
        var x: CGFloat = 0, y: CGFloat = 0, rowH: CGFloat = 0, widest: CGFloat = 0
        for v in subviews {
            let sz = v.sizeThatFits(.unspecified)
            if x > 0 && x + sz.width > maxW { x = 0; y += rowH + spacing; rowH = 0 }
            x += sz.width + spacing; rowH = max(rowH, sz.height); widest = max(widest, x - spacing)
        }
        return CGSize(width: min(widest, maxW), height: y + rowH)
    }
    func placeSubviews(in bounds: CGRect, proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) {
        var x = bounds.minX, y = bounds.minY, rowH: CGFloat = 0
        for v in subviews {
            let sz = v.sizeThatFits(.unspecified)
            if x > bounds.minX && x + sz.width > bounds.maxX { x = bounds.minX; y += rowH + spacing; rowH = 0 }
            v.place(at: CGPoint(x: x, y: y), proposal: ProposedViewSize(sz))
            x += sz.width + spacing; rowH = max(rowH, sz.height)
        }
    }
}

// MARK: - Markdown for replies (headings, lists, quotes, code blocks, inline styles)

struct MarkdownView: View {
    var text: String

    private enum Block { case code(String, String), heading(Int, String), bullet(String, String, Int), quote(String), para(String), rule }

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            ForEach(Array(blocks().enumerated()), id: \.offset) { _, b in view(b) }
        }
        .textSelection(.enabled)
    }

    @ViewBuilder private func view(_ b: Block) -> some View {
        switch b {
        case .code(let lang, let code):
            VStack(alignment: .leading, spacing: 6) {
                HStack {
                    Text(lang.isEmpty ? "code" : lang).font(.system(size: 11)).foregroundStyle(.white.opacity(0.6))
                    Spacer()
                    Button { copy(code) } label: { Image(systemName: "doc.on.doc").font(.system(size: 11)) }
                        .buttonStyle(.plain).foregroundStyle(.white.opacity(0.6)).help("Copy code")
                }
                ScrollView(.horizontal, showsIndicators: false) {
                    Text(code).font(.system(size: 12.5, design: .monospaced)).foregroundStyle(.white.opacity(0.95))
                }
            }
            .padding(12)
            .background(RoundedRectangle(cornerRadius: 12, style: .continuous).fill(.black.opacity(0.28)))
            .overlay(RoundedRectangle(cornerRadius: 12, style: .continuous).strokeBorder(.white.opacity(0.12)))
        case .heading(let level, let t):
            inline(t).font(.system(size: level == 1 ? 19 : level == 2 ? 17 : 15, weight: .semibold)).padding(.top, 4)
        case .bullet(let marker, let t, let indent):
            HStack(alignment: .firstTextBaseline, spacing: 8) {
                Text(marker).foregroundStyle(.white.opacity(0.6))
                inline(t)
            }.padding(.leading, CGFloat(indent) * 16 + 4)
        case .quote(let t):
            inline(t).foregroundStyle(.white.opacity(0.75)).padding(.leading, 10)
                .overlay(Rectangle().fill(.white.opacity(0.4)).frame(width: 2), alignment: .leading)
        case .para(let t):
            inline(t)
        case .rule:
            Rectangle().fill(.white.opacity(0.18)).frame(height: 1).padding(.vertical, 4)
        }
    }

    private func inline(_ s: String) -> Text {
        let opts = AttributedString.MarkdownParsingOptions(interpretedSyntax: .inlineOnlyPreservingWhitespace)
        if let a = try? AttributedString(markdown: s, options: opts) { return Text(a) }
        return Text(s)
    }

    private func copy(_ s: String) {
        NSPasteboard.general.clearContents()
        NSPasteboard.general.setString(s, forType: .string)
    }

    private func blocks() -> [Block] {
        var out: [Block] = []
        let lines = text.replacingOccurrences(of: "\r\n", with: "\n").components(separatedBy: "\n")
        var i = 0
        var para: [String] = []
        func flush() { if !para.isEmpty { out.append(.para(para.joined(separator: "\n"))); para = [] } }
        while i < lines.count {
            let line = lines[i]
            let t = line.trimmingCharacters(in: .whitespaces)
            if t.hasPrefix("```") {
                flush()
                let lang = String(t.dropFirst(3)).trimmingCharacters(in: .whitespaces)
                var code: [String] = []
                i += 1
                while i < lines.count && !lines[i].trimmingCharacters(in: .whitespaces).hasPrefix("```") { code.append(lines[i]); i += 1 }
                i += 1
                out.append(.code(lang, code.joined(separator: "\n")))
                continue
            }
            if t.isEmpty { flush(); i += 1; continue }
            if let r = t.range(of: #"^#{1,6}\s+"#, options: .regularExpression) {
                flush()
                out.append(.heading(t[r].filter { $0 == "#" }.count, String(t[r.upperBound...])))
                i += 1; continue
            }
            if t.range(of: #"^([-*_])\1{2,}$"#, options: .regularExpression) != nil { flush(); out.append(.rule); i += 1; continue }
            if let r = line.range(of: #"^\s*([-*+]|\d+[.)])\s+"#, options: .regularExpression) {
                flush()
                let lead = line.prefix { $0 == " " || $0 == "\t" }.count
                let markerRaw = line[r].trimmingCharacters(in: .whitespaces)
                let marker = markerRaw.first?.isNumber == true ? markerRaw.replacingOccurrences(of: ")", with: ".") : "•"
                out.append(.bullet(marker, String(line[r.upperBound...]), lead / 2))
                i += 1; continue
            }
            if t.hasPrefix(">") { flush(); out.append(.quote(String(t.drop { $0 == ">" || $0 == " " }))); i += 1; continue }
            para.append(line)
            i += 1
        }
        flush()
        return out
    }
}
