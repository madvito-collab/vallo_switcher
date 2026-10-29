// Снимает включённые в системе раскладки EN и RU в JSON-фикстуры для тестов:
// Data/layouts/{ansi,iso}-{en,ru}.json
// Запуск из корня репозитория: swift run dump-layouts

import Carbon
import Foundation
import ValloCore

func enabledLayout(for language: String) -> TISInputSource? {
    let filter = [kTISPropertyInputSourceType as String: kTISTypeKeyboardLayout as String] as CFDictionary
    let list = TISCreateInputSourceList(filter, false).takeRetainedValue() as! [TISInputSource]
    return list.first { source in
        guard let ptr = TISGetInputSourceProperty(source, kTISPropertyInputSourceLanguages) else { return false }
        let langs = Unmanaged<CFArray>.fromOpaque(ptr).takeUnretainedValue() as! [String]
        return langs.first == language
    }
}

func keyboardType(_ wanted: PhysicalKeyboardLayoutType) -> UInt32? {
    (0...255).map(UInt32.init).first { KBGetLayoutType(Int16($0)) == wanted }
}

func table(of source: TISInputSource, kbdType: UInt32) -> [KeyStroke: String] {
    let ptr = TISGetInputSourceProperty(source, kTISPropertyUnicodeKeyLayoutData)!
    let data = Unmanaged<CFData>.fromOpaque(ptr).takeUnretainedValue() as Data
    var result: [KeyStroke: String] = [:]
    data.withUnsafeBytes { raw in
        let layout = raw.bindMemory(to: UCKeyboardLayout.self).baseAddress!
        for keyCode in UInt16(0)...127 {
            for shift in [false, true] {
                for caps in [false, true] {
                    var mods: UInt32 = 0
                    if shift { mods |= UInt32(shiftKey >> 8) }
                    if caps { mods |= UInt32(alphaLock >> 8) }
                    var dead: UInt32 = 0
                    var chars = [UniChar](repeating: 0, count: 4)
                    var len = 0
                    let status = UCKeyTranslate(layout, keyCode, UInt16(kUCKeyActionDown), mods, kbdType,
                                                OptionBits(kUCKeyTranslateNoDeadKeysBit), &dead, 4, &len, &chars)
                    guard status == noErr, len > 0 else { continue }
                    let s = String(utf16CodeUnits: chars, count: len)
                    guard let scalar = s.unicodeScalars.first, scalar.value >= 0x20, scalar.value != 0x7F else { continue }
                    result[KeyStroke(keyCode: keyCode, shift: shift, capsLock: caps)] = s
                }
            }
        }
    }
    return result
}

guard let en = enabledLayout(for: "en"), let ru = enabledLayout(for: "ru") else {
    print("Не нашёл включённые раскладки EN и RU")
    exit(1)
}

let encoder = JSONEncoder()
encoder.outputFormatting = [.prettyPrinted]
let out = URL(fileURLWithPath: FileManager.default.currentDirectoryPath).appendingPathComponent("Data/layouts")

for (name, type) in [("ansi", PhysicalKeyboardLayoutType(kKeyboardANSI)), ("iso", PhysicalKeyboardLayoutType(kKeyboardISO))] {
    guard let kbd = keyboardType(type) else { print("Нет kbdType для \(name)"); exit(1) }
    for (lang, source) in [(Language.en, en), (Language.ru, ru)] {
        let layout = TableKeyLayout(language: lang, table: table(of: source, kbdType: kbd))
        let file = out.appendingPathComponent("\(name)-\(lang.rawValue).json")
        try encoder.encode(layout).write(to: file)
        print("\(file.lastPathComponent): \(layout.table.count) записей (kbdType \(kbd))")
    }
}
