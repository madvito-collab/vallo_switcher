import Carbon
import Foundation
import os
import ValloCore

/// Журнал отладки. Пишет только при включённом режиме отладки и никогда — набранный текст:
/// только виды действий, длины и состояния.
enum DebugLog {
    nonisolated(unsafe) static var enabled = false
    private static let logger = Logger(subsystem: "com.vallo.switcher", category: "debug")

    static func debug(_ message: @autoclosure () -> String) {
        guard enabled else { return }
        let text = message()
        logger.debug("\(text)")
    }
}

let log = DebugLog.self

/// Настоящая раскладка системы: что даёт клавиша на подключённой сейчас клавиатуре.
struct SystemKeyLayout: KeyLayout {
    let language: Language
    let data: Data

    func character(for stroke: KeyStroke) -> String? {
        var mods: UInt32 = 0
        if stroke.shift { mods |= UInt32(shiftKey >> 8) }
        if stroke.capsLock { mods |= UInt32(alphaLock >> 8) }
        var dead: UInt32 = 0
        var chars = [UniChar](repeating: 0, count: 4)
        var len = 0
        // тип клавиатуры (ANSI/ISO/JIS) читаем каждый раз — внешнюю клавиатуру могли подключить
        let kbdType = UInt32(LMGetKbdType())
        let status = data.withUnsafeBytes { raw -> OSStatus in
            let layout = raw.bindMemory(to: UCKeyboardLayout.self).baseAddress!
            return UCKeyTranslate(layout, stroke.keyCode, UInt16(kUCKeyActionDown), mods, kbdType,
                                  OptionBits(kUCKeyTranslateNoDeadKeysBit), &dead, 4, &len, &chars)
        }
        guard status == noErr, len > 0 else { return nil }
        let s = String(utf16CodeUnits: chars, count: len)
        guard let scalar = s.unicodeScalars.first, scalar.value >= 0x20, scalar.value != 0x7F else { return nil }
        return s
    }
}

enum SystemLayoutsError: Error { case missing(String) }

/// Раскладки EN и RU: чтение, переключение, слежка за сменой.
/// Для каждого языка берём ту раскладку, которую человек реально выбирает
/// (у кого-то «Русская», у кого-то «Русская — ПК»).
final class SystemLayouts {
    private(set) var pair: LayoutPair
    private var sources: [Language: TISInputSource]
    /// Смена раскладки (своё переключение тоже приходит сюда, и дважды — движок отбрасывает смену на ту же).
    var onExternalChange: ((Language?) -> Void)?
    /// Человек выбрал другую раскладку того же языка — нужны новые таблицы.
    var onLayoutsChanged: ((LayoutPair) -> Void)?

    init() throws {
        var sources: [Language: TISInputSource] = [:]
        for lang in [Language.en, .ru] {
            guard let source = SystemLayouts.enabledLayout(for: lang) else { throw SystemLayoutsError.missing(lang.rawValue) }
            sources[lang] = source
        }
        // текущая раскладка важнее первой попавшейся
        let now = TISCopyCurrentKeyboardInputSource().takeRetainedValue()
        if let lang = SystemLayouts.language(of: now), SystemLayouts.layoutData(of: now) != nil { sources[lang] = now }
        self.sources = sources
        pair = try SystemLayouts.makePair(sources)

        DistributedNotificationCenter.default().addObserver(
            forName: NSNotification.Name(kTISNotifySelectedKeyboardInputSourceChanged as String),
            object: nil, queue: .main
        ) { [weak self] _ in self?.selectionChanged() }
    }

    private func selectionChanged() {
        let now = TISCopyCurrentKeyboardInputSource().takeRetainedValue()
        let lang = SystemLayouts.language(of: now)
        log.debug("TIS notification: current=\(lang?.rawValue ?? "other")")
        if let lang, SystemLayouts.layoutData(of: now) != nil, SystemLayouts.id(of: now) != SystemLayouts.id(of: sources[lang]!) {
            sources[lang] = now
            if let newPair = try? SystemLayouts.makePair(sources) {
                pair = newPair
                log.debug("layouts changed: \(lang.rawValue) → \(SystemLayouts.id(of: now))")
                onLayoutsChanged?(newPair)
            }
        }
        onExternalChange?(lang)
    }

    /// Текущий язык; nil — стоит третья раскладка (украинская и т.п.), тогда свитчер молчит.
    var current: Language? {
        SystemLayouts.language(of: TISCopyCurrentKeyboardInputSource().takeRetainedValue())
    }

    func select(_ language: Language) {
        guard language != current, let source = sources[language] else { return }
        TISSelectInputSource(source)
    }

    private static func makePair(_ sources: [Language: TISInputSource]) throws -> LayoutPair {
        var layouts: [Language: SystemKeyLayout] = [:]
        for (lang, source) in sources {
            guard let data = layoutData(of: source) else { throw SystemLayoutsError.missing(lang.rawValue) }
            layouts[lang] = SystemKeyLayout(language: lang, data: data)
        }
        return LayoutPair(en: layouts[.en]!, ru: layouts[.ru]!)
    }

    private static func layoutData(of source: TISInputSource) -> Data? {
        guard let ptr = TISGetInputSourceProperty(source, kTISPropertyUnicodeKeyLayoutData) else { return nil }
        return Unmanaged<CFData>.fromOpaque(ptr).takeUnretainedValue() as Data
    }

    private static func id(of source: TISInputSource) -> String {
        guard let ptr = TISGetInputSourceProperty(source, kTISPropertyInputSourceID) else { return "" }
        return Unmanaged<CFString>.fromOpaque(ptr).takeUnretainedValue() as String
    }

    /// Язык раскладки — только для настоящих раскладок EN и RU (не для IME и прочих).
    private static func language(of source: TISInputSource) -> Language? {
        guard let typePtr = TISGetInputSourceProperty(source, kTISPropertyInputSourceType),
              Unmanaged<CFString>.fromOpaque(typePtr).takeUnretainedValue() as String == kTISTypeKeyboardLayout as String,
              let ptr = TISGetInputSourceProperty(source, kTISPropertyInputSourceLanguages) else { return nil }
        let langs = Unmanaged<CFArray>.fromOpaque(ptr).takeUnretainedValue() as! [String]
        return langs.first.flatMap(Language.init(rawValue:))
    }

    private static func enabledLayout(for language: Language) -> TISInputSource? {
        let filter = [kTISPropertyInputSourceType as String: kTISTypeKeyboardLayout as String] as CFDictionary
        let list = TISCreateInputSourceList(filter, false).takeRetainedValue() as! [TISInputSource]
        return list.first { SystemLayouts.language(of: $0) == language }
    }
}
