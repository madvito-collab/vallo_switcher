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

    @Test func wrongShortWordRevertedByNextWord() {
        // «щт» (в RU) → «on»; следом «привет» пришлось вернуть в RU — значит, и «on» было ошибкой
        let engine = Engine(layouts: ansi, judge: smallJudge, currentLanguage: .ru)
        let short = type("on ", engine)
        #expect(short[2] == .consume([.delete(2), .insert("on "), .switchLayout(.en)]))
        let next = type("ghb", engine)
        #expect(next[2] == .consume([.delete(5), .insert("щт при"), .switchLayout(.ru)]))
        #expect(engine.currentLanguage == .ru)
    }

    @Test func keptShortWordFollowsNextWord() {
        // «ща» набрано в EN-раскладке → «of» (настоящее слово, оставлено); следом русское слово —
        // значит, и «of» было «ща»
        let engine = makeEngine()
        #expect(type("of ", engine).allSatisfy { $0 == .passThrough })
        let next = type("ghb", engine)
        #expect(next[2] == .consume([.delete(5), .insert("ща при"), .switchLayout(.ru)]))
    }

    @Test func shortWordKeptWhenNextWordAgrees() {
        // «jr» → «ок», дальше русское слово — это подтверждение, ничего не возвращаем
        let engine = makeEngine()
        type("jr ", engine)
        #expect(type("ghbdtn", engine).allSatisfy { $0 == .passThrough })
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

    @Test func returnChecksWordEnd() {
        let judge = Judge(en: Lexicon(words: ["xyzpq"]), ru: Lexicon(words: ["чняз"]), learned: [])
        let engine = Engine(layouts: ansi, judge: judge, currentLanguage: .en)
        type("xyzp", engine)
        let enter = KeyEvent(stroke: KeyStroke(keyCode: 36), hasCommandControlOrOption: false)
        #expect(engine.handleKey(enter) ==
                .consume([.delete(4), .insert("чняз"), .switchLayout(.ru), .pressKey(36)]))
        #expect(!engine.hasTypedText)
    }

    @Test func returnWithoutSwitchPassesThrough() {
        let engine = makeEngine(auto: false)
        type("ghbd", engine)
        let tab = KeyEvent(stroke: KeyStroke(keyCode: 48), hasCommandControlOrOption: false)
        #expect(engine.handleKey(tab) == .passThrough)
        #expect(!engine.hasTypedText)
    }

    @Test func updateLayoutsResetsBuffer() {
        let engine = makeEngine(auto: false)
        type("ghbd", engine)
        engine.updateLayouts(fixturePair("iso"))
        #expect(!engine.hasTypedText)
    }

    @Test func bufferConversionUpdatesDecision() {
        let engine = makeEngine(auto: false)
        type("ghbdtn", engine)
        _ = engine.handleDoubleShift(selection: nil)
        #expect(engine.lastDecision?.contains("всё набранное") == true)
    }

    @Test func externalLayoutChangeResets() {
        let engine = makeEngine(auto: false)
        type("ghbd", engine)
        engine.externalLayoutChange(to: .ru)
        #expect(engine.currentLanguage == .ru)
        #expect(engine.handleDoubleShift(selection: nil) == [])
    }
}

@Suite struct MisspellingsPipelineTests {
    @Test func switcherLeavesMisspellingsForTypoCorrector() {
        // «воб» нет среди начал русских слов — свитчер переключал раскладку посреди «вобщем», и исправлятор молчал
        let judge = Judge(en: realEN, ru: realRU, learned: [])
        let options = LiveOptions(typography: true, typos: typos)
        for (typed, fixed) in TypoCorrector.misspellings {
            let engine = Engine(layouts: ansi, judge: judge, currentLanguage: .ru)
            var live = LiveTypographer()
            var edit: TypoEdit?
            for stroke in strokesTyping(typed + " ", in: ansi.ru as! TableKeyLayout) {
                let out = engine.handleKey(KeyEvent(stroke: stroke, hasCommandControlOrOption: false))
                #expect(out == .passThrough, "\(typed): свитчер вмешался")
                edit = live.typed(ansi.ru.character(for: stroke)!.first!, hint: .ru, options: options)
            }
            #expect(edit != nil, "\(typed) → \(fixed): не исправлено на лету")
        }
    }
}

@Suite struct AbbreviationTests {
    /// Нажатия для текста с заглавными: заглавная — та же клавиша с Shift.
    func strokes(_ text: String, in layout: TableKeyLayout) -> [KeyStroke] {
        text.map { ch in
            let s = strokesTyping(String(ch.lowercased()), in: layout)[0]
            return ch.isUppercase ? KeyStroke(keyCode: s.keyCode, shift: true, capsLock: false) : s
        }
    }

    func consumed(_ text: String, _ language: Language) -> Bool {
        let engine = Engine(layouts: ansi, judge: Judge(en: realEN, ru: realRU, learned: []), currentLanguage: language)
        let layout = (language == .ru ? ansi.ru : ansi.en) as! TableKeyLayout
        return strokes(text, in: layout).contains {
            engine.handleKey(KeyEvent(stroke: $0, hasCommandControlOrOption: false)) != .passThrough
        }
    }

    @Test func capsAbbreviationsStayInTheirLayout() {
        // «ВГСЧ» → «DUC», «ЖКХ» → «:R{», «PDF» → «ЗВА» — так было; аббревиатуры не переводим
        for w in ["ВГСЧ ", "ЖКХ ", "СПГ ", "ЧС ", "МЧС ", "СНИЛС "] { #expect(!consumed(w, .ru), "\(w)") }
        for w in ["PDF ", "API ", "KPI ", "SDK "] { #expect(!consumed(w, .en), "\(w)") }
    }

    @Test func ordinaryWordsStillSwitch() {
        #expect(consumed("ghbdtn ", .en))
        #expect(consumed("Ghbdtn ", .en))      // заглавная только первая — это не аббревиатура
    }

    /// Что окажется на экране после набора.
    func typed(_ text: String, _ language: Language) -> String {
        let engine = Engine(layouts: ansi, judge: Judge(en: realEN, ru: realRU, learned: []), currentLanguage: language)
        var screen = ""
        // клавиши — по буквам текста (что человек имел в виду), а раскладка на экране — `language`
        let cyrillic = text.contains { Lexicon.isInAlphabet(Character($0.lowercased()), .ru) }
        let layout = (cyrillic ? ansi.ru : ansi.en) as! TableKeyLayout
        for stroke in strokes(text, in: layout) {
            let shownIn = engine.currentLanguage
            switch engine.handleKey(KeyEvent(stroke: stroke, hasCommandControlOrOption: false)) {
            case .passThrough: screen += ansi.layout(shownIn).character(for: stroke) ?? ""
            case .consume(let actions):
                for a in actions {
                    if case .delete(let n) = a { screen.removeLast(n) }
                    if case .insert(let t) = a { screen += t }
                }
            }
        }
        return screen
    }

    @Test func knownAbbreviationsFromOtherLayout() {
        // набрано в чужой раскладке: маленькими — маленькими, хоть одна заглавная — все заглавные
        #expect(typed("pdf ", .ru) == "pdf ")      // на экране было бы «зва»
        #expect(typed("Pdf ", .ru) == "PDF ")
        #expect(typed("PDF ", .ru) == "PDF ")
        #expect(typed("жкх ", .en) == "жкх ")
        #expect(typed("Жкх ", .en) == "ЖКХ ")
        #expect(typed("ui ", .ru) == "ui ")
    }

    @Test func knownAbbreviationCaseInOwnLayout() {
        #expect(typed("Pdf ", .en) == "PDF ")
        #expect(typed("Жкх ", .ru) == "ЖКХ ")
        #expect(typed("pdf ", .en) == "pdf ")
        #expect(typed("Привет ", .ru) == "Привет ")
    }

    @Test func abbreviationsDontLookLikeOtherWords() {
        // аббревиатура, набранная в чужой раскладке, не должна совпадать с настоящим словом того языка
        let pair = ansi
        for (language, list) in Judge.abbreviations {
            let own = (language == .ru ? pair.ru : pair.en) as! TableKeyLayout
            let other = language == .ru ? Language.en : .ru
            let clashes = list.filter { abbr in
                let shown = Converter.render(strokesTyping(abbr, in: own), in: pair.layout(other))
                return (other == .ru ? realRU : realEN).isWord(shown)
            }
            #expect(clashes.isEmpty, "совпали с настоящими словами: \(clashes.sorted())")
        }
    }

    @Test func judgeKnowsAbbreviation() {
        #expect(Judge.isAbbreviation("ВГСЧ"))
        #expect(Judge.isAbbreviation("PDF,"))
        #expect(!Judge.isAbbreviation("Привет"))
        #expect(!Judge.isAbbreviation("Я"))       // одна буква — не аббревиатура
    }
}

@Suite struct YoPipelineTests {
    @Test func switcherLeavesFrequentYoWords() {
        // как с «вобщем»: если свитчер переключит раскладку посреди слова, «ё» молча не поставится
        let judge = Judge(en: realEN, ru: realRU, learned: [])
        let options = LiveOptions(typography: false, typos: nil, yo: yo)
        let text = try! String(contentsOf: repoURL("Data/wordlists/ru-yo.txt"), encoding: .utf8)
        // имена собственные (с заглавной в словаре) строчными не трогаются — их здесь не проверяем
        let frequent = text.split(separator: "\n").filter { $0.first?.isLowercase == true }
            .map { Lexicon.normalize(String($0)) }.filter { typos.isKnown($0) }
        var failed: [String] = []
        for word in frequent {
            let engine = Engine(layouts: ansi, judge: judge, currentLanguage: .ru)
            var live = LiveTypographer()
            var edit: TypoEdit?
            var touched = false
            for stroke in strokesTyping(word + " ", in: ansi.ru as! TableKeyLayout) {
                if engine.handleKey(KeyEvent(stroke: stroke, hasCommandControlOrOption: false)) != .passThrough { touched = true }
                edit = live.typed(ansi.ru.character(for: stroke)!.first!, hint: .ru, options: options)
            }
            if touched || edit == nil { failed.append(touched ? word + " (свитчер)" : word) }
        }
        print("ё-слов среди частых: \(frequent.count)")
        #expect(failed.isEmpty, "свитчер мешает: \(failed.prefix(50)) … всего \(failed.count)")
    }
}

@Suite struct UnitsAfterNumberTests {
    let t = AbbreviationTests()

    @Test func unitsAfterNumberStayRussian() {
        // «руб» — это «he,» в английской раскладке, «шт» — «in», «сек» — «ctr»: после числа это единицы
        for s in ["850руб ", "850 руб ", "100 руб. ", "5 шт ", "5шт ", "30 сек ", "2 млн руб "] {
            #expect(t.typed(s, .ru) == s, "\(s) → \(t.typed(s, .ru))")
        }
    }

    @Test func unitGluedToNumberSwitches() {
        // «850руб» в английской раскладке — «850he,»: «he» — английское слово, «б» — запятая. Слитно с числом — это рубли
        for s in ["850руб ", "20руб, ", "5мин ", "2млн "] {
            #expect(t.typed(s, .en) == s, "\(s) → \(t.typed(s, .en))")
        }
    }

    @Test func unitWithSpaceOrShortStaysEnglish() {
        // с пробелом это может быть английский текст, а «24h» — «24 часа»
        for s in ["at 20 he, said ", "24h ", "5in "] {
            #expect(t.typed(s, .en) == s, "\(s) → \(t.typed(s, .en))")
        }
    }

    @Test func withoutNumberStillSwitches() {
        // «in», набранное в русской раскладке, по-прежнему исправляется
        #expect(t.typed("шт ", .ru) == "in ")
        #expect(t.typed("I live in ", .ru) == "I live in ")
    }
}

@Suite struct HexInEngineTests {
    /// Набор в русской раскладке «№аааааа» и пробел (Shift+3 в русской — «№»).
    @Test func colorTypedInRussian() {
        let engine = Engine(layouts: ansi, judge: Judge(en: realEN, ru: realRU, learned: []), currentLanguage: .ru)
        var screen = ""
        let strokes = strokesTyping("№аааааа ", in: ansi.ru as! TableKeyLayout)
        for stroke in strokes {
            let shownIn = engine.currentLanguage
            switch engine.handleKey(KeyEvent(stroke: stroke, hasCommandControlOrOption: false)) {
            case .passThrough: screen += ansi.layout(shownIn).character(for: stroke) ?? ""
            case .consume(let actions):
                for a in actions {
                    if case .delete(let n) = a { screen.removeLast(n) }
                    if case .insert(let t) = a { screen += t }
                }
            }
        }
        #expect(screen == "#ffffff ")
    }
}

@Suite struct ApostropheLetterTests {
    /// «э» в английской раскладке — апостроф: «ээээээх» = «''''''[». Двойной Shift переводит всё, не только «х»
    /// (найдено при проверке: оставалось «''''''х»).
    @Test func doubleShiftConvertsApostrophes() {
        let engine = Engine(layouts: ansi, judge: Judge(en: realEN, ru: realRU, learned: []), currentLanguage: .en)
        for stroke in strokesTyping("ээээээх", in: ansi.ru as! TableKeyLayout) {
            #expect(engine.handleKey(KeyEvent(stroke: stroke, hasCommandControlOrOption: false)) == .passThrough)
        }
        #expect(engine.handleDoubleShift(selection: nil) == [.delete(7), .insert("ээээээх"), .switchLayout(.ru)])
    }
}

@Suite struct CommaLetterTests {
    let t = AbbreviationTests()

    @Test func wordsStartingWithBInWrongLayout() {
        // «б» в английской раскладке — запятая: «был» = «,sk», свитчер считал его словом из двух букв и не трогал
        for s in ["был ", "бог ", "бок ", "бар ", "были "] {
            #expect(t.typed(s, .en) == s, "\(s) → \(t.typed(s, .en))")
        }
        #expect(t.typed("I was ok ", .en) == "I was ok ")
    }
}
