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
        #expect(merged("когда ты придешь мы пойдем гулять") == "когда ты придёшь, мы пойдём гулять")
        #expect(merged("он сказал что не сможет прийти потому что заболел")
                == "он сказал, что не сможет прийти, потому что заболел")
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

    @Test func modelAddsWordsRejected() {
        // модель дописала слово — не принимаем
        #expect(CommaFilter.merge(original: "ну давай", model: "Ну давай, пожалуйста.", typos: typos, capitalize: false)
                == "ну давай")
    }
}
