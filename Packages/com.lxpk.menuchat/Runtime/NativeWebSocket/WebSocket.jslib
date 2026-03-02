// Embedded from NativeWebSocket by Endel Dreyer, Jiri Hybek
// https://github.com/endel/NativeWebSocket | Apache 2.0 License

var LibraryWebSocket = {
  $webSocketState: {
    instances: {},
    lastId: 0,
    onOpen: null,
    onMessage: null,
    onError: null,
    onClose: null,
    debug: false
  },

  WebSocketSetOnOpen: function(callback) {
    webSocketState.onOpen = callback;
  },

  WebSocketSetOnMessage: function(callback) {
    webSocketState.onMessage = callback;
  },

  WebSocketSetOnError: function(callback) {
    webSocketState.onError = callback;
  },

  WebSocketSetOnClose: function(callback) {
    webSocketState.onClose = callback;
  },

  WebSocketAllocate: function(url) {
    var urlStr = UTF8ToString(url);
    var id = webSocketState.lastId++;
    webSocketState.instances[id] = {
      subprotocols: [],
      url: urlStr,
      ws: null
    };
    return id;
  },

  WebSocketAddSubProtocol: function(instanceId, subprotocol) {
    var subprotocolStr = UTF8ToString(subprotocol);
    webSocketState.instances[instanceId].subprotocols.push(subprotocolStr);
  },

  WebSocketFree: function(instanceId) {
    var instance = webSocketState.instances[instanceId];
    if (!instance) return 0;
    if (instance.ws && instance.ws.readyState < 2)
      instance.ws.close();
    delete webSocketState.instances[instanceId];
    return 0;
  },

  WebSocketConnect: function(instanceId) {
    var instance = webSocketState.instances[instanceId];
    if (!instance) return -1;
    if (instance.ws !== null) return -2;

    instance.ws = new WebSocket(instance.url, instance.subprotocols);
    instance.ws.binaryType = 'arraybuffer';

    instance.ws.onopen = function() {
      if (webSocketState.onOpen)
        Module.dynCall_vi(webSocketState.onOpen, instanceId);
    };

    instance.ws.onmessage = function(ev) {
      if (webSocketState.onMessage === null) return;
      var dataBuffer;
      if (ev.data instanceof ArrayBuffer) {
        dataBuffer = new Uint8Array(ev.data);
      } else {
        dataBuffer = (new TextEncoder()).encode(ev.data);
      }
      var buffer = _malloc(dataBuffer.length);
      HEAPU8.set(dataBuffer, buffer);
      try {
        Module.dynCall_viii(webSocketState.onMessage, instanceId, buffer, dataBuffer.length);
      } finally {
        _free(buffer);
      }
    };

    instance.ws.onerror = function(ev) {
      if (webSocketState.onError) {
        var msg = "WebSocket error.";
        var length = lengthBytesUTF8(msg) + 1;
        var buffer = _malloc(length);
        stringToUTF8(msg, buffer, length);
        try {
          Module.dynCall_vii(webSocketState.onError, instanceId, buffer);
        } finally {
          _free(buffer);
        }
      }
    };

    instance.ws.onclose = function(ev) {
      if (webSocketState.onClose)
        Module.dynCall_vii(webSocketState.onClose, instanceId, ev.code);
      delete instance.ws;
    };

    return 0;
  },

  WebSocketClose: function(instanceId, code, reasonPtr) {
    var instance = webSocketState.instances[instanceId];
    if (!instance) return -1;
    if (!instance.ws) return -3;
    if (instance.ws.readyState === 2) return -4;
    if (instance.ws.readyState === 3) return -5;
    var reason = (reasonPtr ? UTF8ToString(reasonPtr) : undefined);
    try {
      instance.ws.close(code, reason);
    } catch(err) {
      return -7;
    }
    return 0;
  },

  WebSocketSend: function(instanceId, bufferPtr, length) {
    var instance = webSocketState.instances[instanceId];
    if (!instance) return -1;
    if (!instance.ws) return -3;
    if (instance.ws.readyState !== 1) return -6;
    instance.ws.send(HEAPU8.buffer.slice(bufferPtr, bufferPtr + length));
    return 0;
  },

  WebSocketSendText: function(instanceId, message) {
    var instance = webSocketState.instances[instanceId];
    if (!instance) return -1;
    if (!instance.ws) return -3;
    if (instance.ws.readyState !== 1) return -6;
    instance.ws.send(UTF8ToString(message));
    return 0;
  },

  WebSocketGetState: function(instanceId) {
    var instance = webSocketState.instances[instanceId];
    if (!instance) return -1;
    if (instance.ws) return instance.ws.readyState;
    return 3;
  }
};

autoAddDeps(LibraryWebSocket, '$webSocketState');
mergeInto(LibraryManager.library, LibraryWebSocket);
