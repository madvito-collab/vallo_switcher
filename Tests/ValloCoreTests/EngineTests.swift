import Testing
@testable import ValloCore

let ansi = fixturePair("ansi")

func makeEngine(auto: Bool = true) -> Engine {
    let engine = Engine(layouts: ansi, judge: smallJudge, currentLanguage: .en)
    engine.autoSwitchEnabled = auto
    return engine
}

/// Нажимает клавиши, которые в EN-раскладке дают этот текст. Возвращает ответ на каждое нажатие.
@discardableResult
func type(_ text: String, _ engine: Engine) -> [EngineOutput] {
    strokesTyping(text, in: ansi.en as! TableKeyLayout).map {
        engine.handleKey(KeyEvent(stroke: $0, hasCommandControlOrOption: false))
    }
}

let backspace = KeyEvent(stroke: KeyStroke(keyCode: 51), hasCommandControlOrOption: false)

@Suite struct EngineTests {
    @Test func autoSwitchMidWord() {
        let engine = makeEngine()
        let out = type("ghbdtn", engine)
        #expect(out[0] == .passThrough)
        #expect(out[1] == .passThrough)
        #expect(out[2] == .consume([.delete(2), .insert("при"), .switchLayout(.ru)]))
        #expect(out[3...].allSatisfy { $0 == .passThrough })
        #expect(engine.currentLanguage == .ru)
        #expect(engine.lastDecision != nil)
    }

    @Test func undoRightAfterAutoSwitch() {
        let engine = makeEngine()
        type("ghbdtn ", engine)
        // первый откат — только вернуть слово, не запоминать (вдруг Shift нажат случайно)
        #expect(engine.handleDoubleShift(selection: nil) ==
                [.delete(7), .insert("ghbdtn "), .switchLayout(.en)])
        #expect(engine.currentLanguage == .en)
    }

    @Test func secondUndoOfSameWordLearns() {
        let engine = makeEngine()
        type("ghbdtn ", engine)
        _ = engine.handleDoubleShift(selection: nil)
        // не запомнено — второй раз снова исправляется
        let again = type("ghbdtn ", engine)
        #expect(again[2] == .consume([.delete(2), .insert("при"), .switchLayout(.ru)]))
        #expect(engine.handleDoubleShift(selection: nil) ==
                [.delete(7), .insert("ghbdtn "), .switchLayout(.en), .learn("ghbdtn")])
        // запомнено — третий раз не трогаем
        #expect(type("ghbdtn", engine).allSatisfy { $0 == .passThrough })
    }

    @Test func undoOfDifferentWordDoesNotLearn() {
        let engine = makeEngine()
        type("ghbdtn ", engine)
        _ = engine.handleDoubleShift(selection: nil)
        type("ghbd ", engine)
        #expect(engine.handleDoubleShift(selection: nil) ==
                [.delete(5), .insert("ghbd "), .switchLayout(.en)])
    }

    @Test func undoWhileStillTypingWord() {
        let engine = makeEngine()
        type("ghbd", engine)
        #expect(engine.handleDoubleShift(selection: nil) ==
                [.delete(4), .insert("ghbd"), .switchLayout(.en)])
    }

    @Test func undoExpiresOnNextWord() {
        let engine = makeEngine()
        type("ghbdtn x", engine)
        // отката уже нет — сценарий В: всё набранное в другую раскладку
        #expect(engine.handleDoubleShift(selection: nil) ==
                [.delete(8), .insert("ghbdtn x"), .switchLayout(.en)])
    }

    @Test func doubleShiftConvertsWholeRun() {
        let engine = makeEngine(auto: false)
        type("ghbdtn rfr ltkf", engine)
        #expect(engine.handleDoubleShift(selection: nil) ==
                [.delete(15), .insert("привет как дела"), .switchLayout(.ru)])
        #expect(engine.currentLanguage == .ru)
    }

    @Test func doubleShiftSelection() {
        let engine = makeEngine()
        #expect(engine.handleDoubleShift(selection: "руддщ") == [.replaceSelection("hello"), .switchLayout(.en)])
        #expect(engine.handleDoubleShift(selection: "ghbdtn") == [.replaceSelection("привет"), .switchLayout(.ru)])
    }

    @Test func doubleShiftWithNothingDoesNothing() {
        let engine = makeEngine()
        #expect(engine.handleDoubleShift(selection: nil) == [])
        #expect(engine.handleDoubleShift(selection: "") == [])
        #expect(engine.currentLanguage == .en)
    }

    @Test func commandComboResetsBuffer() {
        let engine = makeEngine(auto: false)
        type("ghbd", engine)
        let cmdZ = KeyEvent(stroke: KeyStroke(keyCode: 6), hasCommandControlOrOption: true)
        #expect(engine.handleKey(cmdZ) == .passThrough)
        #expect(engine.handleDoubleShift(selection: nil) == [])
    }

    @Test func navigationResetsBuffer() {
        let engine = makeEngine(auto: false)
        type("ghbd", engine)
        #expect(engine.handleKey(KeyEvent(stroke: KeyStroke(keyCode: 123), hasCommandControlOrOption: false)) == .passThrough)
        #expect(engine.handleDoubleShift(selection: nil) == [])
    }

    @Test func commaInsideWordIsLetter() {
        let engine = makeEngine(auto: false)
        type(",eltn", engine)
        #expect(engine.handleDoubleShift(selection: nil) == [.delete(5), .insert("будет"), .switchLayout(.ru)])
    }

    @Test func backspaceShrinksBuffer() {
        let engine = makeEngine(auto: false)
        type("ghbdtnn", engine)
        #expect(engine.handleKey(backspace) == .passThrough)
        #expect(engine.handleDoubleShift(selection: nil) == [.delete(6), .insert("привет"), .switchLayout(.ru)])
    }

    @Test func shortWordsOnlyFromList() {
        let engine = makeEngine()
        #expect(type("xx ", engine).allSatisfy { $0 == .passThrough })
        let out = type("jr ", makeEngine())
        #expect(out[2] == .consume([.delete(2), .insert("ок "), .switchLayout(.ru)]))
    }

    @Test func autoSwitchDisabled() {
        let engine = makeEngine(auto: false)
        #expect(type("ghbdtn ", engine).allSatisfy { $0 == .passThrough })
    }

    @Test func wordEndSwitch() {
        // начало «xyzp» в EN-словаре есть (xyzpq), поэтому посреди слова ждём; на пробеле — слово RU
        let judge = Judge(en: Lexicon(words: ["xyzpq"]), ru: Lexicon(words: ["чняз"]), learned: [])
        let engine = Engine(layouts: ansi, judge: judge, currentLanguage: .en)
        let out = type("xyzp ", engine)
        #expect(out[0..<4].allSatisfy { $0 == .passThrough })
        #expect(out[4] == .consume([.delete(4), .insert("чняз "), .switchLayout(.ru)]))
    }

    @Test func hasTypedTextTracksBuffer() {
        // есть набранное — значит, свежего выделения быть не может (клик и стрелки сбрасывают буфер)
        let engine = makeEngine(auto: false)
        #expect(!engine.hasTypedText)
        type("ghb", engine)
        #expect(engine.hasTypedText)
        engine.reset()
        #expect(!engine.hasTypedText)
    }

    @Test func canUndoOnlyRightAfterAutoSwitch() {
        let engine = makeEngine()
        #expect(!engine.canUndo)
        type("ghbdtn ", engine)
        #expect(engine.canUndo)
        type("x", engine)
        #expect(!engine.canUndo)
    }

    @Test func duplicateLayoutNotificationKeepsUndo() {
        // Мак присылает уведомление о смене раскладки дважды — второе не должно стирать откат
        let engine = makeEngine()
        type("ghb", engine)
        engine.externalLayoutChange(to: .ru)
        type("dtn ", engine)
        #expect(engine.handleDoubleShift(selection: nil) ==
                [.delete(7), .insert("ghbdtn "), .switchLayout(.en)])
    }

    @Test func externalLayoutChangeResets() {
        let engine = makeEngine(auto: false)
        type("ghbd", engine)
        engine.externalLayoutChange(to: .ru)
        #expect(engine.currentLanguage == .ru)
        #expect(engine.handleDoubleShift(selection: nil) == [])
    }
}
