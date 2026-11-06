import Testing
@testable import ValloCore

@Suite struct HexColorTests {
    @Test func russianLayoutColors() {
        #expect(HexColor.fix("№аааааа", requireHash: true) == "#ffffff")
        #expect(HexColor.fix("#аааааа", requireHash: true) == "#ffffff")
        #expect(HexColor.fix("№1ф2и3с", requireHash: true) == "#1a2b3c")
        #expect(HexColor.fix("№ФФФ", requireHash: true) == "#AAA")
        #expect(HexColor.fix("№вуфвиууа", requireHash: true) == "#deadbeef")
        #expect(HexColor.fix("№aaaaaa", requireHash: true) == "#aaaaaa")      // свитчер уже переключил буквы, № остался
    }

    @Test func notColors() {
        #expect(HexColor.fix("№123456", requireHash: true) == nil)            // номер документа
        #expect(HexColor.fix("#123456", requireHash: true) == nil)            // уже правильно
        #expect(HexColor.fix("#abcdef", requireHash: true) == nil)
        #expect(HexColor.fix("#ааааа", requireHash: true) == nil)             // 5 знаков
        #expect(HexColor.fix("#привет", requireHash: true) == nil)
        #expect(HexColor.fix("аааааа", requireHash: true) == nil)             // без решётки — только в Фигме
    }

    @Test func figmaFieldWithoutHash() {
        #expect(HexColor.fix("аааааа", requireHash: false) == "ffffff")
        #expect(HexColor.fix("ффф", requireHash: false) == "aaa")
        #expect(HexColor.fix("123", requireHash: false) == nil)
        #expect(HexColor.fix("сова", requireHash: false) == nil)
    }
}
