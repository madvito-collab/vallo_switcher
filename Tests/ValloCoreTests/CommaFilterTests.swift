import Testing
@testable import ValloCore

/// Ответы модели SAGE из эксперимента 2026-10-30: исходная фраза ⇥ ответ модели.
let sageOutputs: [String: String] = {
    let text = try! String(contentsOf: repoURL("Data/test/sage-outputs.tsv"), encoding: .utf8)
    var map: [String: String] = [:]
    for line in text.split(separator: "\n") {
        let parts = line.split(separator: "\t", omittingEmptySubsequences: false)
        if parts.count >= 2 { map[String(parts[0])] = String(parts[1]) }
    }
    return map
}()

@Suite struct CommaFilterTests {
    func merged(_ original: String, capitalize: Bool = false) -> String {
        CommaFilter.merge(original: original, model: sageOutputs[original]!, typos: typos, capitalize: capitalize)
    }

    @Test func slangAndJargonKept() {
        #expect(merged("короче я хз что делать") == "короче, я хз что делать")
        #expect(merged("я щас приду ща напишу") == "я щас приду, ща напишу")
        #expect(merged("спс за макет завтра созвонимся") == "спс за макет завтра созвонимся")
        #expect(merged("задеплоил на прод вроде всё ок") == "задеплоил на прод вроде всё ок")
        #expect(merged("пришли пожалуйста скрин ошибки") == "пришли, пожалуйста, скрин ошибки")
    }

    @Test func commasAccepted() {
        #expect(merged("я думаю что это хорошая идея") == "я думаю, что это хорошая идея")
        // «ё» от модели не берём — её ставит наш словарь (свой переключатель, двойные не трогаем)
        #expect(merged("когда ты придешь мы пойдем гулять") == "когда ты придешь, мы пойдем гулять")
        #expect(merged("он сказал что не сможет прийти потому что заболел")
                == "он сказал, что не сможет прийти, потому что заболел")
    }

    @Test func modelYoIgnored() {
        // модель не ставит «ё» в двойных и не откатывает нашу
        #expect(CommaFilter.merge(original: "все пришли", model: "Всё пришли.", typos: typos, capitalize: false) == "все пришли")
        #expect(CommaFilter.merge(original: "ещё не время", model: "еще не время", typos: typos, capitalize: false) == "ещё не время")
    }

    @Test func safeReplacements() {
        #expect(merged("какой то странный баг в приложении") == "какой-то странный баг в приложении")
        #expect(merged("ктото забыл закрыть дверь") == "кто-то забыл закрыть дверь")
        #expect(merged("вобщем всё нормально") == "вобщем, всё нормально")
    }

    @Test func noDotBetweenDigits() {
        #expect(merged("у нас митинг в 15 00") == "у нас митинг в 15 00")
    }

    @Test func capitalizeOption() {
        #expect(merged("спасибо большое ты лучший") == "спасибо большое, ты лучший")
        #expect(merged("спасибо большое ты лучший", capitalize: true) == "Спасибо большое, ты лучший.")
    }

    @Test func ownPunctuationKept() {
        func m(_ o: String, _ model: String) -> String {
            CommaFilter.merge(original: o, model: model, typos: typos, capitalize: false)
        }
        #expect(m("Я дома.", "Я дома.") == "Я дома.")
        #expect(m("Он сказал «привет» и ушёл", "Он сказал «привет» и ушёл.") == "Он сказал «привет» и ушёл")
        #expect(m("см. (пример) тут", "См. (пример) тут.") == "см. (пример) тут")
        #expect(m("Что-то не так...", "Что-то не так...") == "Что-то не так...")
        #expect(m("Привет. Как дела?", "Привет. Как дела?") == "Привет. Как дела?")
        // запятая модели — после закрывающей кавычки
        #expect(m("он сказал «да» и ушёл", "Он сказал «да», и ушёл.") == "он сказал «да», и ушёл")
    }

    @Test func whitespaceKept() {
        let o = "  я думаю  что\tэто идея  "
        #expect(CommaFilter.merge(original: o, model: "Я думаю, что это идея.", typos: typos, capitalize: false)
                == "  я думаю,  что\tэто идея  ")
    }

    @Test func noCommaDotAtEnd() {
        #expect(CommaFilter.merge(original: "привет как дела", model: "привет, как дела, ну", typos: typos, capitalize: true)
                == "Привет, как дела.")
    }

    @Test func modelAddsWordsRejected() {
        // модель дописала слово — не принимаем
        #expect(CommaFilter.merge(original: "ну давай", model: "Ну давай, пожалуйста.", typos: typos, capitalize: false)
                == "ну давай")
    }
}
