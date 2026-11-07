// Keep Unity's pinned linear-space video uploader. Guard the native boundaries
// where a lost GPU context or a temporarily unavailable frame can abort Unity.
// Unity loads its Video.js before project libraries (verified in the Bee link).
if (typeof LibraryManager.library.JS_Video_UpdateToTexture !== 'function') {
  throw new Error('Slop City video texture guard requires Unity Video.js first.');
}
mergeInto(LibraryManager.library, {
  $slopCityNativeVideoUpdateToTexture: LibraryManager.library.JS_Video_UpdateToTexture,
  $slopCityNativeVideoUpdateToTexture__deps: LibraryManager.library.JS_Video_UpdateToTexture__deps.slice(),

  $slopCityReleaseVideoTexture__deps: ['$slopCityDetachVideoDestination', '$slopCityQueueFailedVideoTexture'],
  $slopCityReleaseVideoTexture: function (decoder) {
    var retired = true;
    slopCityDetachVideoDestination();
    if (decoder.slopCityLinearVideoTexture) {
      // Retain this exact owner if deletion fails; decoder retirement retries it.
      var contextLost = typeof GLctx === 'undefined' || !GLctx || (GLctx.isContextLost && GLctx.isContextLost());
      if (!contextLost) {
        try { GLctx.deleteTexture(decoder.slopCityLinearVideoTexture); }
        catch (_) { retired = slopCityQueueFailedVideoTexture(decoder.slopCityLinearVideoTexture); }
      } else retired = slopCityQueueFailedVideoTexture(decoder.slopCityLinearVideoTexture);
    }
    // A full failed-texture queue cannot become permission to lose the only
    // remaining native owner. The retired decoder keeps this handle and its
    // normal drain will retry transfer after earlier releases complete.
    if (!retired) return false;
    delete decoder.slopCityLinearVideoTexture;
    delete decoder.slopCityUploadedDecodedFrame;
    delete decoder.slopCityUploadedTexture;
    delete decoder.slopCityUploadedTextureId;
    delete decoder.slopCityUploadedLinearMode;
    delete decoder.slopCityUploadedPictureSource;
    delete decoder.slopCityLinearVideoWidth;
    delete decoder.slopCityLinearVideoHeight;
    return true;
  },

  $slopCityPauseVideoForGraphicsLoss__deps: ['$videoInstances', '$slopCityRetiredVideoDecoders', '$slopCityCancelCreatedVideoFrame', 'JS_Video_Pause'],
  $slopCityPauseVideoForGraphicsLoss__postset: "Module['slopCityPauseVideoForGraphicsLoss'] = slopCityPauseVideoForGraphicsLoss;",
  $slopCityPauseVideoForGraphicsLoss: function () {
    // Reuse the playback bridge: pause also cancels stale playback intent,
    // pending gesture retries and Unity's loop poll interval for each decoder.
    var firstError;
    function pauseDecoder(id, video, active) {
      try {
        if (video.slopCitySourceAdmissionTimer) {
          clearTimeout(video.slopCitySourceAdmissionTimer);
          delete video.slopCitySourceAdmissionTimer;
        }
        try { slopCityCancelCreatedVideoFrame(video); } catch (_) {}
        if (active) _JS_Video_Pause(id);
        else {
          try { video.pause(); } catch (_) {}
          try { video.muted = true; } catch (_) {}
        }
      } catch (error) { if (!firstError) firstError = error; }
    }
    Object.keys(videoInstances).forEach(function (id) {
      pauseDecoder(Number(id), videoInstances[id], true);
    });
    Object.keys(slopCityRetiredVideoDecoders).forEach(function (id) {
      pauseDecoder(id, slopCityRetiredVideoDecoders[id], false);
    });
    if (firstError) throw firstError;
  },

  $slopCityFailedVideoTextures: [],
  $slopCityFailedVideoTextureCap: 48,
  $slopCityVideoTextureRetirementBlocked: false,
  $slopCityFailedVideoTextureDrainInterval: null,
  $slopCityDrainFailedVideoTextures__deps: ['$slopCityFailedVideoTextures', '$slopCityVideoTextureRetirementBlocked'],
  $slopCityDrainFailedVideoTextures: function (budgetMs) {
    if (typeof budgetMs !== 'number') budgetMs = 8;
    if (typeof GLctx === 'undefined' || !GLctx || (GLctx.isContextLost && GLctx.isContextLost())) return false;
    var deadline = Date.now() + budgetMs;
    var attempts = 0;
    for (var i = slopCityFailedVideoTextures.length - 1; i >= 0 && attempts < 16 && Date.now() < deadline; --i) {
      ++attempts;
      try {
        GLctx.deleteTexture(slopCityFailedVideoTextures[i]);
        slopCityFailedVideoTextures.splice(i, 1);
      } catch (_) { /* Refused deletion remains owned; block replacement. */ }
    }
    var clear = slopCityFailedVideoTextures.length === 0;
    if (clear) slopCityVideoTextureRetirementBlocked = false;
    return clear;
  },
  $slopCityArmFailedVideoTextureDrain__deps: ['$slopCityFailedVideoTextures', '$slopCityFailedVideoTextureDrainInterval', '$slopCityDrainFailedVideoTextures'],
  $slopCityArmFailedVideoTextureDrain: function () {
    if (!slopCityFailedVideoTextures.length || slopCityFailedVideoTextureDrainInterval || typeof setInterval !== 'function') return;
    slopCityFailedVideoTextureDrainInterval = setInterval(function () {
      slopCityDrainFailedVideoTextures(8);
      if (!slopCityFailedVideoTextures.length && slopCityFailedVideoTextureDrainInterval) {
        clearInterval(slopCityFailedVideoTextureDrainInterval);
        slopCityFailedVideoTextureDrainInterval = null;
      }
    }, 250);
  },
  $slopCityQueueFailedVideoTexture__deps: ['$slopCityFailedVideoTextures', '$slopCityFailedVideoTextureCap', '$slopCityVideoTextureRetirementBlocked', '$slopCityDrainFailedVideoTextures', '$slopCityArmFailedVideoTextureDrain'],
  $slopCityQueueFailedVideoTexture: function (texture) {
    if (!texture || slopCityFailedVideoTextures.indexOf(texture) >= 0) return true;
    slopCityDrainFailedVideoTextures(2);
    if (slopCityFailedVideoTextures.length < slopCityFailedVideoTextureCap) {
      slopCityFailedVideoTextures.push(texture);
      slopCityArmFailedVideoTextureDrain();
      return true;
    }
    // Do not grow an unreachable retry list forever. A full queue stops new
    // allocations until retirement catches up; a direct deletion still wins.
    try {
      if (typeof GLctx !== 'undefined' && GLctx && !GLctx.isContextLost()) {
        GLctx.deleteTexture(texture);
        return true;
      }
    } catch (_) { }
    slopCityVideoTextureRetirementBlocked = true;
    return false;
  },
  $jsVideoCreateTexture2D__deps: ['$slopCityQueueFailedVideoTexture', '$slopCityVideoTextureRetirementBlocked', '$slopCityDrainFailedVideoTextures'],
  $jsVideoCreateTexture2D: function () {
    var texture = null;
    try {
      if (!slopCityDrainFailedVideoTextures() || slopCityVideoTextureRetirementBlocked) throw new Error('A previous video texture is still retiring.');
      texture = GLctx.createTexture();
      if (!texture) throw new Error('The browser could not allocate a video texture.');
      GLctx.bindTexture(GLctx.TEXTURE_2D, texture);
      GLctx.texParameteri(GLctx.TEXTURE_2D, GLctx.TEXTURE_WRAP_S, GLctx.CLAMP_TO_EDGE);
      GLctx.texParameteri(GLctx.TEXTURE_2D, GLctx.TEXTURE_WRAP_T, GLctx.CLAMP_TO_EDGE);
      GLctx.texParameteri(GLctx.TEXTURE_2D, GLctx.TEXTURE_MIN_FILTER, GLctx.LINEAR);
      return texture;
    } catch (cause) {
      if (texture) {
        try { GLctx.deleteTexture(texture); }
        catch (_) { slopCityQueueFailedVideoTexture(texture); }
      }
      var error = new Error('The browser could not prepare a video texture.');
      error.slopCityVideoTextureAllocationFailure = true;
      throw error;
    }
  },

  // Unity's linear video path assumes all shader/buffer/FBO allocations succeed.
  // Prepare those exact resources transactionally before entering that path.
  $slopCityVideoVertexArray: null,
  // JS_Video_UpdateToTexture is synchronous, so all linear conversions are
  // serialized. One shared intermediate avoids retaining a full GPU texture
  // per live decoder when several screens have different aspect ratios.
  $slopCityLinearVideoWidth: 0,
  $slopCityLinearVideoHeight: 0,
  $slopCityPendingVideoGraphics: [],
  $slopCityDrainPendingVideoGraphics__deps: ['$slopCityPendingVideoGraphics'],
  $slopCityDrainPendingVideoGraphics: function () {
    var methods = { vertex: 'deleteShader', fragment: 'deleteShader', program: 'deleteProgram',
      buffer: 'deleteBuffer', framebuffer: 'deleteFramebuffer', vertexArray: 'deleteVertexArray' };
    for (var i = slopCityPendingVideoGraphics.length - 1; i >= 0; --i) {
      var owner = slopCityPendingVideoGraphics[i];
      Object.keys(methods).forEach(function (key) {
        if (!owner[key]) return;
        try {
          // A lost/unavailable context cannot have released this native owner.
          // Keep the exact handle in the bounded retry set until a usable
          // context actually accepts its deletion.
          if (typeof GLctx === 'undefined' || !GLctx || (GLctx.isContextLost && GLctx.isContextLost())) return;
          GLctx[methods[key]](owner[key]);
          owner[key] = null;
        } catch (_) { /* Preserve each failed native owner independently. */ }
      });
      if (!Object.keys(methods).some(function (key) { return !!owner[key]; })) slopCityPendingVideoGraphics.splice(i, 1);
    }
    return slopCityPendingVideoGraphics.length === 0;
  },
  $slopCityPrepareLinearVideo__deps: ['$s2lProgram', '$s2lVertexPositionNDC', '$s2lVBO', '$s2lFBO', '$slopCityVideoVertexArray', '$slopCityPendingVideoGraphics', '$slopCityDrainPendingVideoGraphics'],
  $slopCityPrepareLinearVideo: function () {
    if (!slopCityDrainPendingVideoGraphics()) return false;
    if (s2lProgram && s2lVBO && s2lFBO && slopCityVideoVertexArray) return true;
    var previousBuffer = GLctx.getParameter(GLctx.ARRAY_BUFFER_BINDING);
    var owner = {};
    slopCityPendingVideoGraphics.push(owner);
    var attribute = s2lVertexPositionNDC;
    try {
      if (!s2lProgram) {
        owner.vertex = GLctx.createShader(GLctx.VERTEX_SHADER);
        owner.fragment = GLctx.createShader(GLctx.FRAGMENT_SHADER);
        owner.program = GLctx.createProgram();
        if (!owner.vertex || !owner.fragment || !owner.program) return false;
        GLctx.shaderSource(owner.vertex, 'precision lowp float; attribute vec2 vertexPositionNDC; varying vec2 vTexCoords; void main(){vTexCoords=vertexPositionNDC*vec2(0.5)+vec2(0.5);gl_Position=vec4(vertexPositionNDC,0.0,1.0);}');
        GLctx.shaderSource(owner.fragment, 'precision mediump float; uniform sampler2D colorMap; varying vec2 vTexCoords; void main(){vec4 c=texture2D(colorMap,vTexCoords);gl_FragColor=vec4(c.rgb*(c.rgb*(c.rgb*0.305306011+0.682171111)+0.012522878),c.a);}');
        GLctx.compileShader(owner.vertex); GLctx.compileShader(owner.fragment);
        if (!GLctx.getShaderParameter(owner.vertex, GLctx.COMPILE_STATUS) || !GLctx.getShaderParameter(owner.fragment, GLctx.COMPILE_STATUS)) return false;
        GLctx.attachShader(owner.program, owner.vertex); GLctx.attachShader(owner.program, owner.fragment); GLctx.linkProgram(owner.program);
        if (!GLctx.getProgramParameter(owner.program, GLctx.LINK_STATUS)) return false;
        var attribute = GLctx.getAttribLocation(owner.program, 'vertexPositionNDC');
        if (attribute < 0) return false;
      }
      if (!s2lVBO) {
        owner.buffer = GLctx.createBuffer();
        if (!owner.buffer) return false;
        GLctx.bindBuffer(GLctx.ARRAY_BUFFER, owner.buffer);
        GLctx.bufferData(GLctx.ARRAY_BUFFER, new Float32Array([1,1,-1,1,-1,-1,-1,-1,1,-1,1,1]), GLctx.STATIC_DRAW);
      }
      if (!s2lFBO) { owner.framebuffer = GLctx.createFramebuffer(); if (!owner.framebuffer) return false; }
      // The Web build requires WebGL2. A private VAO keeps Unity's enabled
      // attributes, pointers and instancing divisors out of the conversion draw.
      if (!slopCityVideoVertexArray) { owner.vertexArray = GLctx.createVertexArray(); if (!owner.vertexArray) return false; }
      if (owner.program) { s2lProgram = owner.program; s2lVertexPositionNDC = attribute; owner.program = null; }
      if (owner.buffer) { s2lVBO = owner.buffer; owner.buffer = null; }
      if (owner.framebuffer) { s2lFBO = owner.framebuffer; owner.framebuffer = null; }
      if (owner.vertexArray) { slopCityVideoVertexArray = owner.vertexArray; owner.vertexArray = null; }
      return true;
    } catch (_) { return false; } finally {
      // Failed partial owners remain in the only retirement queue until every
      // native object is actually released. A later allocation is blocked above.
      // Cleanup candidates separately: a refused shader deletion must not lose
      // the remaining program/buffer/FBO owners or admit another allocation.
      slopCityDrainPendingVideoGraphics();
      GLctx.bindBuffer(GLctx.ARRAY_BUFFER, previousBuffer);
    }
  },

  $slopCityDetachVideoDestination__deps: ['$s2lFBO'],
  $slopCityDetachVideoDestination: function () {
    if (!s2lFBO || typeof GLctx === 'undefined' || !GLctx || GLctx.isContextLost()) return;
    var previous = GLctx.getParameter(GLctx.DRAW_FRAMEBUFFER_BINDING);
    try {
      GLctx.bindFramebuffer(GLctx.DRAW_FRAMEBUFFER, s2lFBO);
      GLctx.framebufferTexture2D(GLctx.DRAW_FRAMEBUFFER, GLctx.COLOR_ATTACHMENT0, GLctx.TEXTURE_2D, null, 0);
    } finally { GLctx.bindFramebuffer(GLctx.DRAW_FRAMEBUFFER, previous); }
  },

  $slopCityReleaseVideoGraphics__deps: ['$s2lProgram', '$s2lVBO', '$s2lFBO', '$s2lTexture', '$slopCityVideoVertexArray', '$slopCityLinearVideoWidth', '$slopCityLinearVideoHeight', '$slopCityDetachVideoDestination', '$slopCityQueueFailedVideoTexture', '$slopCityDrainFailedVideoTextures', '$slopCityDrainPendingVideoGraphics'],
  $slopCityReleaseVideoGraphics: function () {
    var firstError;
    function clean(action) { try { action(); } catch (error) { if (!firstError) firstError = error; } }
    clean(function () { slopCityDetachVideoDestination(); });
    clean(function () {
      if (!s2lTexture) return;
      var contextLost = typeof GLctx === 'undefined' || !GLctx || (GLctx.isContextLost && GLctx.isContextLost());
      var retired = true;
      if (!contextLost) {
        try { GLctx.deleteTexture(s2lTexture); }
        catch (_) { retired = slopCityQueueFailedVideoTexture(s2lTexture); }
      } else retired = slopCityQueueFailedVideoTexture(s2lTexture);
      if (!retired) {
        var pending = new Error('The shared video texture is still retiring.');
        pending.slopCityVideoTextureRetirementBlocked = true;
        throw pending;
      }
      s2lTexture = null;
      slopCityLinearVideoWidth = slopCityLinearVideoHeight = 0;
    });
    clean(function () { if (s2lFBO && GLctx && !GLctx.isContextLost()) GLctx.deleteFramebuffer(s2lFBO); s2lFBO = null; });
    clean(function () { if (s2lVBO && GLctx && !GLctx.isContextLost()) GLctx.deleteBuffer(s2lVBO); s2lVBO = null; });
    clean(function () { if (s2lProgram && GLctx && !GLctx.isContextLost()) GLctx.deleteProgram(s2lProgram); s2lProgram = null; });
    clean(function () { if (slopCityVideoVertexArray && GLctx && !GLctx.isContextLost()) GLctx.deleteVertexArray(slopCityVideoVertexArray); slopCityVideoVertexArray = null; });
    clean(function () { if (!slopCityDrainFailedVideoTextures()) throw new Error('Video textures are still retiring.'); });
    clean(function () { if (!slopCityDrainPendingVideoGraphics()) throw new Error('Video graphics are still retiring.'); });
    if (firstError) throw firstError;
  },

  $slopCityVideoGraphicsScratch: {
    unpackNames: null,
    unpack: null,
    capabilities: null,
    enabled: null,
    viewport: null,
    colorMask: null
  },

  $slopCityCaptureVideoGraphicsState: function (linear) {
    var scratch = slopCityVideoGraphicsScratch;
    if (!scratch.unpackNames) {
      scratch.unpackNames = [GLctx.UNPACK_FLIP_Y_WEBGL, GLctx.UNPACK_PREMULTIPLY_ALPHA_WEBGL,
        GLctx.UNPACK_COLORSPACE_CONVERSION_WEBGL, GLctx.UNPACK_ALIGNMENT,
        GLctx.UNPACK_ROW_LENGTH, GLctx.UNPACK_IMAGE_HEIGHT, GLctx.UNPACK_SKIP_PIXELS,
        GLctx.UNPACK_SKIP_ROWS, GLctx.UNPACK_SKIP_IMAGES];
      scratch.unpack = new Array(scratch.unpackNames.length);
      scratch.capabilities = [GLctx.BLEND, GLctx.CULL_FACE, GLctx.DEPTH_TEST, GLctx.STENCIL_TEST,
        GLctx.SCISSOR_TEST, GLctx.RASTERIZER_DISCARD, GLctx.SAMPLE_COVERAGE, GLctx.SAMPLE_ALPHA_TO_COVERAGE];
      scratch.enabled = new Array(scratch.capabilities.length);
      scratch.viewport = new Array(4);
      scratch.colorMask = new Array(4);
    }
    var state = {
      activeTexture: GLctx.getParameter(GLctx.ACTIVE_TEXTURE),
      activeBinding: GLctx.getParameter(GLctx.TEXTURE_BINDING_2D),
      unpackBuffer: GLctx.getParameter(GLctx.PIXEL_UNPACK_BUFFER_BINDING),
      unpackNames: scratch.unpackNames,
      unpack: scratch.unpack,
      linear: !!linear
    };
    for (var i = 0; i < state.unpackNames.length; ++i)
      state.unpack[i] = GLctx.getParameter(state.unpackNames[i]);
    if (linear) {
      var viewport = GLctx.getParameter(GLctx.VIEWPORT);
      state.viewport = scratch.viewport;
      state.viewport[0] = viewport[0]; state.viewport[1] = viewport[1];
      state.viewport[2] = viewport[2]; state.viewport[3] = viewport[3];
      state.drawFramebuffer = GLctx.getParameter(GLctx.DRAW_FRAMEBUFFER_BINDING);
      state.readFramebuffer = GLctx.getParameter(GLctx.READ_FRAMEBUFFER_BINDING);
      state.program = GLctx.getParameter(GLctx.CURRENT_PROGRAM);
      state.arrayBuffer = GLctx.getParameter(GLctx.ARRAY_BUFFER_BINDING);
      state.vertexArray = GLctx.getParameter(GLctx.VERTEX_ARRAY_BINDING);
      var colorMask = GLctx.getParameter(GLctx.COLOR_WRITEMASK);
      state.colorMask = scratch.colorMask;
      state.colorMask[0] = colorMask[0]; state.colorMask[1] = colorMask[1];
      state.colorMask[2] = colorMask[2]; state.colorMask[3] = colorMask[3];
      state.capabilities = scratch.capabilities;
      state.enabled = scratch.enabled;
      for (var j = 0; j < state.capabilities.length; ++j)
        state.enabled[j] = GLctx.isEnabled(state.capabilities[j]);
    }
    GLctx.activeTexture(GLctx.TEXTURE0);
    state.texture0 = GLctx.getParameter(GLctx.TEXTURE_BINDING_2D);
    state.sampler0 = GLctx.getParameter(GLctx.SAMPLER_BINDING);
    return state;
  },

  $slopCityRestoreVideoGraphicsState: function (state, previousTexture, texture) {
    if (!state || GLctx.isContextLost()) return;
    if (state.linear) {
      GLctx.bindFramebuffer(GLctx.DRAW_FRAMEBUFFER, state.drawFramebuffer);
      GLctx.bindFramebuffer(GLctx.READ_FRAMEBUFFER, state.readFramebuffer);
      GLctx.viewport(state.viewport[0], state.viewport[1], state.viewport[2], state.viewport[3]);
      GLctx.useProgram(state.program);
      GLctx.bindVertexArray(state.vertexArray);
      GLctx.bindBuffer(GLctx.ARRAY_BUFFER, state.arrayBuffer);
      GLctx.colorMask(state.colorMask[0], state.colorMask[1], state.colorMask[2], state.colorMask[3]);
      for (var i = 0; i < state.capabilities.length; ++i) {
        if (state.enabled[i]) GLctx.enable(state.capabilities[i]); else GLctx.disable(state.capabilities[i]);
      }
    }
    for (var j = 0; j < state.unpackNames.length; ++j)
      GLctx.pixelStorei(state.unpackNames[j], state.unpack[j]);
    GLctx.bindBuffer(GLctx.PIXEL_UNPACK_BUFFER, state.unpackBuffer);
    GLctx.activeTexture(GLctx.TEXTURE0);
    // A dimension change replaces Unity's destination object. Restore its live
    // replacement if that object was bound on entry, never a deleted GL handle.
    GLctx.bindTexture(GLctx.TEXTURE_2D, previousTexture && state.texture0 === previousTexture
      ? GL.textures[texture] : state.texture0);
    GLctx.bindSampler(0, state.sampler0);
    GLctx.activeTexture(state.activeTexture);
    if (state.activeTexture !== GLctx.TEXTURE0)
      GLctx.bindTexture(GLctx.TEXTURE_2D, previousTexture && state.activeBinding === previousTexture
        ? GL.textures[texture] : state.activeBinding);
  },

  // Retain Unity's linear conversion. Desktop WebKit can reuse same-size
  // storage; mobile must let the video source define its upload dimensions.
  $slopCityWebkitVideoUpload__deps: ['$videoInstances', '$jsVideoCreateTexture2D', '$hasSRGBATextures', '$s2lTexture', '$s2lFBO', '$s2lVBO', '$s2lProgram', '$s2lVertexPositionNDC', '$slopCityLinearVideoWidth', '$slopCityLinearVideoHeight', '$slopCityUsesMobileVideo', '$slopCityDetachVideoDestination', '$slopCityQueueFailedVideoTexture'],
  $slopCityWebkitVideoUpload: function (video, texture, linear) {
    var v = videoInstances[video];
    var mobile = slopCityUsesMobileVideo(v);
    if (typeof v.requestVideoFrameCallback === 'function' ? !v.isLoaded : !v.isReady) return false;
    var sameTime = v.lastUpdateTextureTime === v.currentTime;
    // iOS decoders omit requestVideoFrameCallback; currentTime can repeat while
    // frames still advance. Keep uploading while playback is active.
    if (!(v.videoWidth > 0 && v.videoHeight > 0) || v.seeking
      || (sameTime && (!mobile || v.paused || v.ended))) return false;
    // Preparation/play still use iOS metadata readiness. Texture publication
    // requires data for the current frame so zero-filled storage cannot count
    // as the first picture and replace a screen's retained still image.
    if (mobile && !(v.readyState >= 2)) return false;
    v.lastUpdateTextureTime = v.currentTime;
    GLctx.pixelStorei(GLctx.UNPACK_FLIP_Y_WEBGL, true);
    var format = linear && !hasSRGBATextures ? GLctx.RGB : GLctx.RGBA;
    var resized = v.previousUploadedWidth != v.videoWidth || v.previousUploadedHeight != v.videoHeight;
    if (resized) {
      if (linear) slopCityDetachVideoDestination();
      var previousDestination = GL.textures[texture];
      try { GLctx.deleteTexture(previousDestination); }
      catch (_) {
        if (previousDestination && !slopCityQueueFailedVideoTexture(previousDestination)) {
          var blocked = new Error('The previous video destination is still retiring.');
          blocked.slopCityVideoTextureAllocationFailure = true;
          blocked.slopCityVideoTextureRetirementBlocked = true;
          throw blocked;
        }
      }
      var destination = jsVideoCreateTexture2D();
      destination.name = texture;
      GL.textures[texture] = destination;
      v.previousUploadedWidth = v.videoWidth;
      v.previousUploadedHeight = v.videoHeight;
      if (linear) GLctx.texImage2D(GLctx.TEXTURE_2D, 0, format, v.videoWidth, v.videoHeight, 0, format, GLctx.UNSIGNED_BYTE, null);
    }
    var defineStorage = resized;
    if (linear) {
      if (!s2lTexture) {
        s2lTexture = jsVideoCreateTexture2D();
        defineStorage = true;
      } else {
        GLctx.bindTexture(GLctx.TEXTURE_2D, s2lTexture);
        defineStorage = slopCityLinearVideoWidth !== v.videoWidth || slopCityLinearVideoHeight !== v.videoHeight;
      }
    } else {
      GLctx.bindTexture(GLctx.TEXTURE_2D, GL.textures[texture]);
    }
    if (mobile) {
      // iOS videoWidth/videoHeight describe the aspect-corrected picture, not
      // necessarily the source pixels copied by WebGL (for example, a 480x426
      // duet with 639:640 pixel aspect). Define storage from the video source
      // once, then reuse it with subimages so WebKit does not retain a fresh
      // GPU allocation on every frame during gameplay. Fall back to a full
      // source-sized upload when Safari rejects the copy.
      if (defineStorage || resized) {
        GLctx.texImage2D(GLctx.TEXTURE_2D, 0, format, format, GLctx.UNSIGNED_BYTE, v);
        v.slopCityWebkitFullUpload = true;
      } else if (v.slopCityWebkitFullUpload) {
        GLctx.texImage2D(GLctx.TEXTURE_2D, 0, format, format, GLctx.UNSIGNED_BYTE, v);
      } else {
        try {
          GLctx.texSubImage2D(GLctx.TEXTURE_2D, 0, 0, 0, format, GLctx.UNSIGNED_BYTE, v);
        } catch (subError) {
          v.slopCityWebkitFullUpload = true;
          GLctx.texImage2D(GLctx.TEXTURE_2D, 0, format, format, GLctx.UNSIGNED_BYTE, v);
        }
      }
    } else if (defineStorage) {
      GLctx.texImage2D(GLctx.TEXTURE_2D, 0, format, format, GLctx.UNSIGNED_BYTE, v);
    } else {
      GLctx.texSubImage2D(GLctx.TEXTURE_2D, 0, 0, 0, format, GLctx.UNSIGNED_BYTE, v);
    }
    if (linear) {
      slopCityLinearVideoWidth = v.videoWidth;
      slopCityLinearVideoHeight = v.videoHeight;
    }
    GLctx.pixelStorei(GLctx.UNPACK_FLIP_Y_WEBGL, false);
    if (linear) {
      GLctx.bindFramebuffer(GLctx.FRAMEBUFFER, s2lFBO);
      GLctx.framebufferTexture2D(GLctx.FRAMEBUFFER, GLctx.COLOR_ATTACHMENT0, GLctx.TEXTURE_2D, GL.textures[texture], 0);
      if (GLctx.checkFramebufferStatus(GLctx.FRAMEBUFFER) !== GLctx.FRAMEBUFFER_COMPLETE) {
        var incomplete = new Error('Video conversion target is incomplete.');
        incomplete.slopCityVideoTextureAllocationFailure = true;
        throw incomplete;
      }
      GLctx.bindTexture(GLctx.TEXTURE_2D, s2lTexture);
      GLctx.viewport(0, 0, v.videoWidth, v.videoHeight);
      GLctx.useProgram(s2lProgram);
      GLctx.bindBuffer(GLctx.ARRAY_BUFFER, s2lVBO);
      GLctx.enableVertexAttribArray(s2lVertexPositionNDC);
      GLctx.vertexAttribPointer(s2lVertexPositionNDC, 2, GLctx.FLOAT, false, 0, 0);
      GLctx.disable(GLctx.CULL_FACE);
      GLctx.drawArrays(GLctx.TRIANGLES, 0, 6);
      // The caller restores the actual render target and viewport, including
      // the smaller world target used by Low, rather than assuming the canvas.
    }
    return true;
  },

  JS_Video_UpdateToTexture__proxy: 'sync',
  JS_Video_UpdateToTexture__sig: 'iiii',
  JS_Video_UpdateToTexture__deps: ['$videoInstances', '$s2lTexture', '$jsIsWebkit', '$slopCityWebkitVideoUpload', '$slopCityNativeVideoUpdateToTexture', '$slopCityPauseVideoForGraphicsLoss', '$slopCityPrepareLinearVideo', '$slopCityUsesMobileVideo', '$slopCityVideoVertexArray', '$slopCityLinearVideoWidth', '$slopCityLinearVideoHeight', '$slopCityVideoGraphicsScratch', '$slopCityCaptureVideoGraphicsState', '$slopCityRestoreVideoGraphicsState', '$slopCityDetachVideoDestination', '$slopCityQueueFailedVideoTexture', '$slopCityWatchVideoPicture', '$slopCityHasCurrentVideoPicture', '$slopCityFailVideoPlayback', 'JS_Video_Pause'],
  JS_Video_UpdateToTexture: function (video, texture, adjustToLinearspace) {
    var decoder = videoInstances[video];
    if (!decoder || decoder.slopCityDestroyed || typeof GLctx === 'undefined' || !GLctx || GLctx.isContextLost()) return false;
    if (!decoder.slopCityTextureRequested) {
      decoder.slopCityTextureRequested = true;
      var intent = decoder.slopCityPlaybackIntent;
      if (intent && intent.pictureDeadline && !intent.pictureWatch && !slopCityHasCurrentVideoPicture(decoder, intent))
        slopCityWatchVideoPicture(video, decoder, intent, intent.pictureDeadline);
    }
    if (decoder.slopCityTextureRetryAfter > Date.now()) return false;
    // This gates only pixel uploads, not JS_Video_IsReady or JS_Video_Play.
    // Unity prepares and starts APIOnly playback independently of GetTexture.
    // HAVE_CURRENT_DATA also handles a missed/late loadeddata event.
    if (slopCityUsesMobileVideo(decoder) && !(decoder.readyState >= 2)) return false;
    var destinationChanged = decoder.slopCityUploadedTextureId !== undefined
      && (decoder.slopCityUploadedTextureId !== texture
        || decoder.slopCityUploadedTexture !== GL.textures[texture]
        || decoder.slopCityUploadedLinearMode !== !!adjustToLinearspace);
    // requestVideoFrameCallback may be throttled for a hidden video while its
    // native currentTime advances. Preserve Unity's actual-time upload contract;
    // a stale callback counter must not pin the world to its first picture.
    // Avoid querying GL state for duplicate frames. A changed destination still
    // receives the same frame once the native decoder has current-frame data.
    var mobileDecoder = slopCityUsesMobileVideo(decoder);
    var samePlaybackTime = decoder.lastUpdateTextureTime === decoder.currentTime;
    if (!(decoder.videoWidth > 0 && decoder.videoHeight > 0) || decoder.seeking
      || (!destinationChanged && samePlaybackTime
        && (!mobileDecoder || decoder.paused || decoder.ended))) return false;
    if (decoder.videoWidth > (slopCityUsesMobileVideo(decoder) ? 480 : 4096) || decoder.videoHeight > (slopCityUsesMobileVideo(decoder) ? 480 : 4096) || decoder.videoWidth * decoder.videoHeight > 8388608) {
      slopCityFailVideoPlayback(video, decoder, decoder.slopCityPlaybackIntent);
      return false;
    }
    if (adjustToLinearspace && !slopCityPrepareLinearVideo()) {
      _JS_Video_Pause(video);
      decoder.slopCityTextureRetryAfter = Date.now() + 5000;
      return false;
    }
    // Keep metadata-stage preparation and playback independent of this upload.
    // A frame that becomes unavailable during transfer is contained below.
    var previousTime = decoder.lastUpdateTextureTime;
    var previousTexture = GL.textures[texture];
    if (destinationChanged) {
      // Unity keys its original dedup/dimensions only by decoder. A new target,
      // recycled GL handle, or conversion mode needs this same frame uploaded.
      delete decoder.lastUpdateTextureTime;
      delete decoder.previousUploadedWidth;
      delete decoder.previousUploadedHeight;
    }
    var replacingTexture = decoder.previousUploadedWidth != decoder.videoWidth
      || decoder.previousUploadedHeight != decoder.videoHeight;
    var graphicsState = null;
    try {
      graphicsState = slopCityCaptureVideoGraphicsState(adjustToLinearspace);
      GLctx.bindBuffer(GLctx.PIXEL_UNPACK_BUFFER, null);
      var unpackDefaults = [false, false, GLctx.BROWSER_DEFAULT_WEBGL, 4, 0, 0, 0, 0, 0];
      for (var i = 0; i < graphicsState.unpackNames.length; ++i)
        GLctx.pixelStorei(graphicsState.unpackNames[i], unpackDefaults[i]);
      GLctx.bindSampler(0, null);
      if (adjustToLinearspace) {
        GLctx.bindVertexArray(slopCityVideoVertexArray);
        GLctx.colorMask(true, true, true, true);
        for (var j = 0; j < graphicsState.capabilities.length; ++j)
          GLctx.disable(graphicsState.capabilities[j]);
      }
      var uploaded = (jsIsWebkit() || slopCityUsesMobileVideo(decoder))
        ? slopCityWebkitVideoUpload(video, texture, adjustToLinearspace)
        : slopCityNativeVideoUpdateToTexture(video, texture, adjustToLinearspace);
      if (uploaded) {
        decoder.slopCityUploadedDecodedFrame = decoder.slopCityDecodedFrame;
        decoder.slopCityUploadedTexture = GL.textures[texture];
        decoder.slopCityUploadedTextureId = texture;
        decoder.slopCityUploadedLinearMode = !!adjustToLinearspace;
        decoder.slopCityUploadedPictureSource = decoder.src;
        decoder.slopCityVideoUploadSerial = (decoder.slopCityVideoUploadSerial || 0) + 1;
      }
      return uploaded;
    } catch (error) {
      var frameUnavailable = typeof DOMException === 'function' && error instanceof DOMException
        && (error.name === 'InvalidStateError' || error.name === 'SecurityError' || error.name === 'NotSupportedError');
      if (!frameUnavailable && !(error && error.slopCityVideoTextureAllocationFailure)) throw error;
      if (error.name !== 'InvalidStateError') {
        _JS_Video_Pause(video);
        decoder.slopCityTextureRetryAfter = Date.now() + 5000;
      }
      // Native Unity deletes the old destination before allocating its replacement.
      // A failed destination allocation must not leave that deleted GPU object in
      // Unity's table. Scratch allocation failures keep the newly assigned target,
      // or the unchanged valid target when its dimensions already matched.
      if (error.slopCityVideoTextureAllocationFailure && !error.slopCityVideoTextureRetirementBlocked && replacingTexture
        && GL.textures[texture] === previousTexture) GL.textures[texture] = null;
      // The engine records time and flips unpacking before allocating/uploading.
      // Roll those changes back so the same valid frame can be retried safely.
      decoder.lastUpdateTextureTime = previousTime;
      delete decoder.previousUploadedWidth;
      delete decoder.previousUploadedHeight;
      slopCityLinearVideoWidth = slopCityLinearVideoHeight = 0;
      return false;
    } finally {
      // Detach before Unity can retire its destination: an attached, deleted
      // texture retains native storage until the FBO releases that reference.
      try { if (adjustToLinearspace) slopCityDetachVideoDestination(); }
      catch (_) {
        // Keep the FBO and decoder owners for retirement/retry. A temporary
        // detach failure cannot skip restoring Unity's render state.
        decoder.slopCityTextureRetryAfter = Date.now() + 5000;
        var attachedDestination = GL.textures[texture];
        if (attachedDestination) slopCityQueueFailedVideoTexture(attachedDestination);
      } finally { slopCityRestoreVideoGraphicsState(graphicsState, previousTexture, texture); }
    }
  }
});
