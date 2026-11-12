import Foundation
import OnnxRuntimeBindings
import Tokenizers
import ValloCore

/// Модель запятых SAGE (ONNX): энкодер + декодер без кэша, жадная генерация.
/// Запускается на Маке без интернета, ~30 мс на фразу. Ответ потом проходит через `CommaFilter`.
final class CommaModel: @unchecked Sendable {
    static let maxTokens = 128
    static let decoderStart: Int64 = 0
    static let endOfText: Int64 = 2
    /// Служебные токены, которые не выводим: <pad>, <s>, </s>, <LM>.
    static let special: Set<Int> = [0, 1, 2, 50357]

    private let tokenizer: Tokenizer
    private let env: ORTEnv
    private let encoder: ORTSession
    private let decoder: ORTSession
    private let vocab: Int

    private init(tokenizer: Tokenizer, env: ORTEnv, encoder: ORTSession, decoder: ORTSession, vocab: Int) {
        self.tokenizer = tokenizer
        self.env = env
        self.encoder = encoder
        self.decoder = decoder
        self.vocab = vocab
    }

    /// Папка распакованной модели: encoder_model.onnx, decoder_model.onnx, tokenizer.json, config.json.
    static func load(directory: URL) async throws -> CommaModel {
        let tokenizer = try await AutoTokenizer.from(modelFolder: directory)
        let env = try ORTEnv(loggingLevel: .warning)
        let options = try ORTSessionOptions()
        let encoder = try ORTSession(env: env, modelPath: directory.appendingPathComponent("encoder_model.onnx").path,
                                     sessionOptions: options)
        let decoder = try ORTSession(env: env, modelPath: directory.appendingPathComponent("decoder_model.onnx").path,
                                     sessionOptions: options)
        let config = try JSONSerialization.jsonObject(with: Data(contentsOf: directory.appendingPathComponent("config.json")))
        let vocab = ((config as? [String: Any])?["vocab_size"] as? Int) ?? 50364
        return CommaModel(tokenizer: tokenizer, env: env, encoder: encoder, decoder: decoder, vocab: vocab)
    }

    /// Ответ модели на фразу (как есть, без фильтра). Вызывать не на главном потоке.
    /// Жадная генерация, но продолжение угадывается по входу (`DraftLookup`) и проверяется пачкой:
    /// один прогон декодера принимает сразу несколько токенов — ответ тот же, прогонов в 2–3 раза меньше.
    func run(_ text: String) throws -> String {
        let ids = tokenizer.encode(text: text).map(Int64.init)
        let mask = try Self.int64Tensor(Array(repeating: 1, count: ids.count))
        let encoded = try encoder.run(withInputs: ["input_ids": try Self.int64Tensor(ids), "attention_mask": mask],
                                      outputNames: ["last_hidden_state"], runOptions: nil)
        guard let hidden = encoded["last_hidden_state"] else { return text }
        var out: [Int64] = []
        generation: while out.count < Self.maxTokens {
            let draft = DraftLookup.draft(output: out, source: ids)
            let input = [Self.decoderStart] + out + draft
            let result = try decoder.run(withInputs: ["input_ids": try Self.int64Tensor(input),
                                                      "encoder_hidden_states": hidden,
                                                      "encoder_attention_mask": mask],
                                         outputNames: ["logits"], runOptions: nil)
            guard let logits = result["logits"] else { break }
            let data = try logits.tensorData() as Data
            // строка logits № base + j предсказывает токен ответа № base + j
            let base = out.count
            for j in 0...draft.count {
                let next = Self.argmax(data, row: base + j, vocab: vocab)
                if next == Self.endOfText { break generation }
                out.append(next)
                if out.count >= Self.maxTokens { break generation }
                if j == draft.count || next != draft[j] { break }
            }
        }
        let tokens = out.map(Int.init).filter { !Self.special.contains($0) }
        return tokenizer.decode(tokens: tokens)
    }

    private static func int64Tensor(_ values: [Int64]) throws -> ORTValue {
        var v = values
        let data = NSMutableData(bytes: &v, length: v.count * MemoryLayout<Int64>.size)
        return try ORTValue(tensorData: data, elementType: .int64, shape: [1, NSNumber(value: v.count)])
    }

    /// Самый вероятный токен в строке `row` таблицы logits.
    private static func argmax(_ data: Data, row: Int, vocab: Int) -> Int64 {
        data.withUnsafeBytes { raw in
            let f = raw.bindMemory(to: Float.self)
            let base = row * vocab
            var best = 0
            for i in 1..<vocab where f[base + i] > f[base + best] { best = i }
            return Int64(best)
        }
    }
}
