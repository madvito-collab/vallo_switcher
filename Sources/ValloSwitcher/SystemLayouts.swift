import Carbon
import Foundation
import os
import ValloCore

let log = Logger(subsystem: "com.vallo.switcher", category: "debug")

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

/// Раскладки EN и RU, включённые в системе: чтение, переключение, слежка за сменой.
final class SystemLayouts {
    private(set) var pair: LayoutPair
    private let sources: [Language: TISInputSource]
    /// Раскладку сменили не мы.
    /// Своё переключение тоже приходит сюда (и дважды) — движок сам отбрасывает смену на ту же раскладку.
    var onExternalChange: ((Language) -> Void)?

    init() throws {
        var sources: [Language: TISInputSource] = [:]
        var layouts: [Language: SystemKeyLayout] = [:]
        for lang in [Language.en, .ru] {
            guard let source = SystemLayouts.enabledLayout(for: lang),
                  let ptr = TISGetInputSourceProperty(source, kTISPropertyUnicodeKeyLayoutData) else {
                throw SystemLayoutsError.missing(lang.rawValue)
            }
            sources[lang] = source
            layouts[lang] = SystemKeyLayout(language: lang,
                                            data: Unmanaged<CFData>.fromOpaque(ptr).takeUnretainedValue() as Data)
        }
        self.sources = sources
        pair = LayoutPair(en: layouts[.en]!, ru: layouts[.ru]!)

        DistributedNotificationCenter.default().addObserver(
            forName: NSNotification.Name(kTISNotifySelectedKeyboardInputSourceChanged as String),
            object: nil, queue: .main
        ) { [weak self] _ in
            guard let self else { return }
            log.debug("TIS notification: current=\(self.current.rawValue, privacy: .public)")
            self.onExternalChange?(self.current)
        }
    }

    var current: Language {
        let source = TISCopyCurrentKeyboardInputSource().takeRetainedValue()
        return SystemLayouts.language(of: source) ?? .en
    }

    func select(_ language: Language) {
        guard language != current, let source = sources[language] else { return }
        TISSelectInputSource(source)
    }

    private static func language(of source: TISInputSource) -> Language? {
        guard let ptr = TISGetInputSourceProperty(source, kTISPropertyInputSourceLanguages) else { return nil }
        let langs = Unmanaged<CFArray>.fromOpaque(ptr).takeUnretainedValue() as! [String]
        return langs.first.flatMap(Language.init(rawValue:))
    }

    private static func enabledLayout(for language: Language) -> TISInputSource? {
        let filter = [kTISPropertyInputSourceType as String: kTISTypeKeyboardLayout as String] as CFDictionary
        let list = TISCreateInputSourceList(filter, false).takeRetainedValue() as! [TISInputSource]
        return list.first { SystemLayouts.language(of: $0) == language }
    }
}
