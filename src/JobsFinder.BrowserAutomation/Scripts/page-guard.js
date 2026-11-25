// Runs in every frame before the page's own scripts. It stops a form from being submitted and removes the ways to talk
// to a server that a normal apply form does not need. A hostile page may work around it: the guarantee is EgressGate.
(() => {
  window.addEventListener(
    'submit',
    (event) => {
      event.preventDefault();
      event.stopImmediatePropagation();
    },
    true,
  );

  const ignore = { value: () => undefined, writable: false, configurable: false };
  Object.defineProperty(HTMLFormElement.prototype, 'submit', ignore);
  Object.defineProperty(HTMLFormElement.prototype, 'requestSubmit', ignore);

  for (const name of ['WebSocket', 'WebTransport', 'SharedWorker', 'RTCPeerConnection', 'webkitRTCPeerConnection']) {
    if (name in window) {
      Object.defineProperty(window, name, { value: undefined, writable: false, configurable: false });
    }
  }
})();
