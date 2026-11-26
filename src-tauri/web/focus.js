(() => {
  if (window.top !== window || location.origin !== "https://web.oopz.cn" || window.__oopzDesktopImages) return;

  const storageKey = "oopz-desktop.hidden-images.v1";
  const protectedArea = "a,button,input,select,textarea,label,summary,dialog,audio,video,[contenteditable]:not([contenteditable=false]),[tabindex],[onclick],[aria-live],[role=alert],[role=status],[role=button],[role=dialog],[role=link],[role=menuitem],[role=switch],[role=checkbox],[role=slider],[role=tab]";
  const eligible = `img[src]:not([srcset]):not(picture *):not(:is(${protectedArea})):not(:is(${protectedArea}) *)`;
  // Query strings and fragments may identify different images or carry signed tokens.
  const isPersistent = source => !/[?#]/.test(source);
  let paths = new Set();
  let sheet;
  let closePicker;
  try {
    const stored = JSON.parse(localStorage.getItem(storageKey));
    if (Array.isArray(stored)) paths = new Set(stored.filter(path => typeof path === "string" && path.length > 0 && isPersistent(path)));
  } catch {
    // Private sessions may have no persistent storage.
  }

  function apply() {
    if (paths.size) {
      sheet ??= new CSSStyleSheet();
      const rules = Array.from(paths, path => {
        const src = CSS.escape(path);
        return `${eligible}[src="${src}"] { display: none !important; }`;
      });
      sheet.replaceSync(rules.join("\n"));
      if (!document.adoptedStyleSheets.includes(sheet)) document.adoptedStyleSheets = [...document.adoptedStyleSheets, sheet];
    } else if (sheet) {
      document.adoptedStyleSheets = document.adoptedStyleSheets.filter(current => current !== sheet);
      sheet = undefined;
    }
  }

  function save() {
    apply();
    try {
      const persistent = [...paths].filter(isPersistent);
      if (persistent.length) localStorage.setItem(storageKey, JSON.stringify(persistent));
      else localStorage.removeItem(storageKey);
    } catch {
      // The current page still retains the selection.
    }
  }

  function imagePath(image) {
    if (!image?.matches(eligible)) return null;
    const source = image.getAttribute("src");
    if (!source.trim()) return null;
    let url;
    try {
      url = new URL(source, document.baseURI);
    } catch {
      return null;
    }
    if (!/^https?:$/.test(url.protocol) || url.username || url.password) return null;
    return source;
  }

  function choose() {
    if (closePicker) return closePicker();
    if (!document.body) return;
    const overlay = document.createElement("div");
    overlay.style.cssText = "all:initial;position:fixed;inset:0;z-index:2147483647;cursor:crosshair";
    const shadow = overlay.attachShadow({ mode: "closed" });
    const message = document.createElement("div");
    message.style.cssText = "position:absolute;top:12px;left:50%;transform:translateX(-50%);background:#182026;color:white;border-radius:6px;padding:10px 16px;font:14px/1.5 system-ui;text-align:center";
    message.textContent = "点选图片以隐藏同图。Tab 选择，Enter 确认，Esc 取消。";
    const status = document.createElement("div");
    status.setAttribute("role", "status");
    message.append(status);
    const cancel = document.createElement("button");
    cancel.type = "button";
    cancel.textContent = "取消";
    cancel.style.cssText = "margin-inline-start:8px;cursor:pointer";
    const controls = document.createElement("div");
    controls.append(cancel);
    message.append(controls);
    const outline = document.createElement("div");
    outline.style.cssText = "position:absolute;pointer-events:none;outline:3px solid #167ddc;background:#187fe329;display:none";
    shadow.append(outline, message);
    const previousFocus = document.activeElement;
    let selected;

    function highlight(image) {
      if (selected !== image) {
        const label = image?.alt || "已选中图片";
        status.textContent = image ? label + (isPersistent(imagePath(image)) ? "" : "（仅在本页隐藏）") : "";
      }
      selected = image;
      outline.style.display = selected ? "block" : "none";
      if (!selected) return;
      const { left, top, width, height } = selected.getBoundingClientRect();
      Object.assign(outline.style, { left: left + "px", top: top + "px", width: width + "px", height: height + "px" });
    }

    function atPointer(event) {
      const { left, top, right, bottom } = message.getBoundingClientRect();
      if (event.clientX >= left && event.clientX <= right && event.clientY >= top && event.clientY <= bottom) return undefined;
      const target = document.elementsFromPoint(event.clientX, event.clientY).find(element => element !== overlay);
      return imagePath(target) ? target : undefined;
    }

    function confirm(image) {
      const path = imagePath(image);
      if (!path) return;
      paths.add(path);
      closePicker();
      save();
    }

    function keyboard(event) {
      // Let the focused cancel button handle Enter and Space.
      if ((event.key === "Enter" || event.key === " ") && !selected) return;
      if (!["Tab", "Enter", "Escape"].includes(event.key)) return;
      event.preventDefault();
      event.stopImmediatePropagation();
      if (event.key === "Escape") return closePicker();
      if (event.key === "Enter") return confirm(selected);
      const images = Array.from(document.images).filter(image => {
        if (!imagePath(image)) return false;
        const rect = image.getBoundingClientRect();
        return rect.width && rect.height && rect.top < innerHeight && rect.bottom > 0 &&
          rect.left < innerWidth && rect.right > 0 && getComputedStyle(image).visibility === "visible";
      });
      if (!images.length) return highlight(undefined);
      const index = selected ? images.indexOf(selected) : (event.shiftKey ? 0 : -1);
      highlight(images[(index + (event.shiftKey ? -1 : 1) + images.length) % images.length]);
    }

    closePicker = () => {
      window.removeEventListener("keydown", keyboard, true);
      overlay.remove();
      closePicker = undefined;
      if (previousFocus instanceof HTMLElement && previousFocus.isConnected) previousFocus.focus({ preventScroll: true });
    };
    cancel.onclick = event => {
      event.stopPropagation();
      closePicker();
    };
    overlay.onpointermove = event => highlight(atPointer(event));
    overlay.onclick = event => {
      event.stopPropagation();
      const image = atPointer(event);
      if (image) confirm(image);
      else status.textContent = "请选择一张独立图片。";
    };
    document.body.append(overlay);
    cancel.focus({ preventScroll: true });
    window.addEventListener("keydown", keyboard, true);
  }

  window.__oopzDesktopImages = {
    choose,
    reset() {
      closePicker?.();
      paths.clear();
      save();
    },
  };
  apply();
})();
