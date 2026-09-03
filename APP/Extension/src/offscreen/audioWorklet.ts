declare const sampleRate: number;

declare abstract class AudioWorkletProcessor {
  readonly port: MessagePort;
  constructor(options?: AudioWorkletNodeOptions);
  abstract process(
    inputs: Float32Array[][],
    outputs: Float32Array[][],
    parameters: Record<string, Float32Array>,
  ): boolean;
}

declare function registerProcessor(
  name: string,
  processorCtor: typeof AudioWorkletProcessor,
): void;

interface ChunkProcessorOptions extends AudioWorkletNodeOptions {
  processorOptions?: {
    chunkDurationMs?: number;
  };
}

class PcmChunkProcessor extends AudioWorkletProcessor {
  private readonly chunk: Float32Array;
  private offset = 0;

  constructor(options?: ChunkProcessorOptions) {
    super(options);
    const duration = options?.processorOptions?.chunkDurationMs ?? 150;
    this.chunk = new Float32Array(Math.round(sampleRate * duration / 1_000));
  }

  process(inputs: Float32Array[][], outputs: Float32Array[][]): boolean {
    const inputChannels = inputs[0];
    const outputChannels = outputs[0];
    const frameLength = inputChannels?.[0]?.length ?? 0;

    for (let frameIndex = 0; frameIndex < frameLength; frameIndex += 1) {
      let monoSample = 0;
      for (const channel of inputChannels ?? []) {
        monoSample += channel[frameIndex] ?? 0;
      }
      monoSample /= Math.max(1, inputChannels?.length ?? 0);

      for (const output of outputChannels ?? []) {
        output[frameIndex] = monoSample;
      }

      this.chunk[this.offset] = monoSample;
      this.offset += 1;
      if (this.offset === this.chunk.length) {
        const completed = this.chunk.slice();
        this.port.postMessage(completed, [completed.buffer]);
        this.offset = 0;
      }
    }

    return true;
  }
}

registerProcessor("translink-pcm-chunker", PcmChunkProcessor);
