/* DNX Camera Server — reproductor de cámara (WebRTC con fallback MJPEG). */
(function () {
  'use strict';

  function el(tag, cls) {
    var e = document.createElement(tag);
    if (cls) e.className = cls;
    return e;
  }

  function DnxPlayer(container, cameraId, opts) {
    this.container = container;
    this.cameraId = cameraId;
    this.opts = Object.assign({ muted: true, autoplay: true }, opts || {});
    this.video = null;
    this.img = null;
    this.pc = null;
    this.stopped = false;
    this.mode = null;
    this._failTimer = null;
  }

  DnxPlayer.prototype.start = async function () {
    this.stopped = false;
    this._clear();
    try {
      await this._startWebRtc();
    } catch (e) {
      console.warn('WebRTC falló, usando MJPEG', e);
      this._startMjpeg();
    }
  };

  DnxPlayer.prototype._clear = function () {
    if (this._failTimer) { clearTimeout(this._failTimer); this._failTimer = null; }
    if (this.pc) { try { this.pc.close(); } catch (e) { } this.pc = null; }
    if (this.video) { this.video.srcObject = null; this.video.remove(); this.video = null; }
    if (this.img) { this.img.src = ''; this.img.remove(); this.img = null; }
    this.container.innerHTML = '';
  };

  DnxPlayer.prototype._startWebRtc = async function () {
    var self = this;
    var video = el('video');
    video.autoplay = true;
    video.playsInline = true;
    video.muted = this.opts.muted;
    this.video = video;
    this.container.appendChild(video);

    var pc = new RTCPeerConnection({ iceServers: [] });
    this.pc = pc;
    pc.addTransceiver('video', { direction: 'recvonly' });
    pc.addTransceiver('audio', { direction: 'recvonly' });

    pc.ontrack = function (ev) {
      if (ev.streams && ev.streams[0]) {
        video.srcObject = ev.streams[0];
      } else {
        if (!video.srcObject) video.srcObject = new MediaStream();
        video.srcObject.addTrack(ev.track);
      }
      video.play().catch(function () { });
    };

    pc.onconnectionstatechange = function () {
      if (pc.connectionState === 'failed' || pc.connectionState === 'disconnected') {
        self._failTimer = setTimeout(function () {
          if (!self.stopped && self.mode === 'webrtc') self._startMjpeg();
        }, 4000);
      }
    };

    var offer = await pc.createOffer();
    await pc.setLocalDescription(offer);

    var res = await fetch('/api/cameras/' + encodeURIComponent(this.cameraId) + '/webrtc/offer', {
      method: 'POST',
      headers: { 'Content-Type': 'application/sdp' },
      body: offer.sdp,
      credentials: 'same-origin'
    });

    if (!res.ok) throw new Error('webrtc-offer-' + res.status);
    var answer = await res.text();
    if (!answer || answer.indexOf('v=') !== 0) throw new Error('webrtc-answer-invalida');

    await pc.setRemoteDescription({ type: 'answer', sdp: answer });
    this.mode = 'webrtc';
  };

  DnxPlayer.prototype._startMjpeg = function () {
    if (this.stopped) return;
    if (this.pc) { try { this.pc.close(); } catch (e) { } this.pc = null; }
    if (this.video) { this.video.srcObject = null; this.video.remove(); this.video = null; }
    this.container.innerHTML = '';

    var img = el('img');
    img.alt = 'cámara';
    img.src = '/api/cameras/' + encodeURIComponent(this.cameraId) + '/mjpeg?ts=' + Date.now();
    this.img = img;
    this.container.appendChild(img);
    this.mode = 'mjpeg';
  };

  DnxPlayer.prototype.setMuted = function (muted) {
    if (this.video) this.video.muted = muted;
  };

  DnxPlayer.prototype.stop = function () {
    this.stopped = true;
    this._clear();
    this.mode = null;
  };

  /* Zoom digital en cliente: no es zoom de cámara. */
  function attachZoom(wrap, video, initial) {
    var state = { scale: 1, x: 0, y: 0 };
    if (initial) Object.assign(state, initial);

    function apply() {
      video.style.transform =
        'translate(' + state.x + 'px,' + state.y + 'px) scale(' + state.scale + ')';
    }

    wrap.addEventListener('wheel', function (ev) {
      ev.preventDefault();
      var delta = ev.deltaY < 0 ? 0.15 : -0.15;
      state.scale = Math.min(6, Math.max(1, state.scale + delta));
      if (state.scale === 1) { state.x = 0; state.y = 0; }
      apply();
    }, { passive: false });

    var dragging = false, sx = 0, sy = 0, ox = 0, oy = 0;
    wrap.addEventListener('pointerdown', function (ev) {
      if (state.scale <= 1) return;
      dragging = true; sx = ev.clientX; sy = ev.clientY; ox = state.x; oy = state.y;
      wrap.setPointerCapture(ev.pointerId);
      wrap.style.cursor = 'grabbing';
    });
    wrap.addEventListener('pointermove', function (ev) {
      if (!dragging) return;
      state.x = ox + (ev.clientX - sx);
      state.y = oy + (ev.clientY - sy);
      apply();
    });
    wrap.addEventListener('pointerup', function () { dragging = false; wrap.style.cursor = ''; });

    apply();
    return {
      get: function () { return Object.assign({}, state); },
      reset: function () { state.scale = 1; state.x = 0; state.y = 0; apply(); }
    };
  }

  window.DnxPlayer = DnxPlayer;
  window.DnxZoom = attachZoom;
})();
