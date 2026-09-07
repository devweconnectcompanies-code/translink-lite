window.transLinkSpeech = (() => {
  let audio = null;
  const stop = () => {
    if (!audio) return;
    audio.pause();
    audio.src = "";
    audio = null;
  };
  return {
    unlock: async () => {
      const context = new AudioContext();
      await context.resume();
      await context.close();
    },
    stop,
    play: async (streamReference, contentType) => {
      stop();
      const bytes = await streamReference.arrayBuffer();
      const url = URL.createObjectURL(new Blob([bytes], { type: contentType }));
      try {
        audio = new Audio(url);
        await audio.play();
        await new Promise((resolve, reject) => {
          audio.onended = resolve;
          audio.onerror = reject;
        });
      } finally {
        stop();
        URL.revokeObjectURL(url);
      }
    },
  };
})();
