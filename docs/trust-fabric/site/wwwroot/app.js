import { marked } from "/vendor/marked/marked.esm.js";
import mermaid from "/vendor/mermaid/mermaid.esm.min.mjs";

const locale = window.location.pathname.toLowerCase().startsWith("/zh-tw") ? "zh-TW" : "en";
const contentPath = locale === "zh-TW"
  ? "/content/README.zh-TW.md"
  : "/content/README.md";

const copy = {
  en: {
    title: "AI Trust Fabric Architecture Specification",
    subtitle: "Architecture Specification",
    contents: "Contents",
    loading: "Loading specification...",
    error: "The specification could not be loaded. Please refresh the page.",
    footer: "Technology-neutral governance for trustworthy AI systems."
  },
  "zh-TW": {
    title: "AI Trust Fabric 架構規格",
    subtitle: "架構規格",
    contents: "文件目錄",
    loading: "正在載入架構規格...",
    error: "無法載入架構規格，請重新整理頁面。",
    footer: "為可信任 AI 系統提供技術中立的治理架構。"
  }
};

const walkthroughCopy = {
  en: {
    label: "Architecture component walkthrough",
    step: "Step",
    of: "of",
    controls: "Primary controls",
    output: "Primary output",
    previous: "Previous",
    next: "Next step",
    restart: "Start again",
    fullscreen: "Full screen",
    exitFullscreen: "Exit full screen",
    steps: [
      {
        title: "AI Application Registry",
        summary: "Establish the approved purpose and operating boundary before any AI workload reaches production.",
        controls: ["Immutable manifest version", "Named owners, purpose and risk tier", "Approved models, data, tools, agents and regions"],
        output: "Versioned application manifest and control baseline"
      },
      {
        title: "AI Gateway",
        summary: "Govern every model inference request without coupling the application to one model provider.",
        controls: ["Caller and workload authentication", "Provider, model, capability and region allowlists", "Safety, token, quota, cost and trace limits"],
        output: "Normalized model-call decision and audit event"
      },
      {
        title: "Retrieval & Data Gateway",
        summary: "Ensure AI systems retrieve only data the delegated subject may use for the declared purpose.",
        controls: ["Document, row, field and tenant authorization", "Classification, masking and purpose limitation", "Source version, citation and provenance capture"],
        output: "Authorized retrieval result with verifiable provenance"
      },
      {
        title: "Tool & Action Gateway",
        summary: "Place policy enforcement immediately before an operation can change an external system.",
        controls: ["Strict tool schema and operation scope", "Amount, destination, time and environment limits", "Idempotency, approval and compensation references"],
        output: "Bounded action authorization and outcome evidence"
      },
      {
        title: "Agent Interaction Gateway",
        summary: "Keep delegated work bounded when agents communicate, fan out or cross organizational boundaries.",
        controls: ["Peer identity, endpoint and capability validation", "Delegated scope, hop, retry and token budgets", "Recursion, fan-out and cross-boundary transfer policy"],
        output: "Traceable agent task with preserved delegation context"
      },
      {
        title: "Policy Decision Point",
        summary: "Evaluate normalized facts against versioned policy while keeping enforcement consistent across gateways.",
        controls: ["Allow, Deny, RequireApproval or obligations", "Deterministic decision ID and explanation", "Tested bundles, scoped exceptions and emergency blocks"],
        output: "Versioned, explainable policy decision"
      },
      {
        title: "Approval Service",
        summary: "Introduce accountable human judgment when policy identifies a consequential or exceptional action.",
        controls: ["Requester, approver and segregation-of-duties checks", "Evidence shown and decision expiration", "One-time approval bound to the exact action digest"],
        output: "Non-replayable approval authorization and decision record"
      },
      {
        title: "Evaluation & Release Gate",
        summary: "Require both software assurance and AI-specific evidence before an exact system version is released.",
        controls: ["Correctness, security, privacy and agent-behavior tests", "Model, prompt, tool, data and policy version binding", "Thresholds, rollback and kill-switch exercises"],
        output: "Signed release authorization for exact versions"
      },
      {
        title: "Audit Evidence Plane",
        summary: "Reconstruct material AI executions without turning ordinary operational logs into a sensitive-content warehouse.",
        controls: ["Structured identity, delegation and decision events", "Metadata-first evidence with a protected sensitive vault", "Integrity, retention, access audit and evidence packaging"],
        output: "Privacy-aware, tamper-evident execution record"
      }
    ]
  },
  "zh-TW": {
    label: "架構元件逐步導覽",
    step: "步驟",
    of: "共",
    controls: "主要控制",
    output: "主要產出",
    previous: "上一步",
    next: "下一步",
    restart: "重新開始",
    fullscreen: "全螢幕",
    exitFullscreen: "離開全螢幕",
    steps: [
      {
        title: "AI Application Registry",
        summary: "在任何 AI workload 進入 production 前，先建立核准的業務目的、責任歸屬與運作邊界。",
        controls: ["不可變更的 Manifest 版本", "具名 owner、業務目的與風險等級", "核准的模型、資料、工具、Agent 與區域"],
        output: "版本化 Application Manifest 與控制基線"
      },
      {
        title: "AI Gateway",
        summary: "治理每一次模型推論要求，同時讓應用程式不必綁定單一模型供應商。",
        controls: ["Caller 與 workload 身分驗證", "Provider、model、capability 與 region allowlist", "安全、token、quota、成本與 trace 限制"],
        output: "正規化的 model-call 決策與 audit event"
      },
      {
        title: "Retrieval & Data Gateway",
        summary: "確保 AI 只取得原始委派主體基於聲明目的而有權使用的資料。",
        controls: ["文件、資料列、欄位與 tenant 授權", "資料分類、遮罩與 purpose limitation", "來源版本、citation 與 provenance 紀錄"],
        output: "具可驗證 provenance 的授權檢索結果"
      },
      {
        title: "Tool & Action Gateway",
        summary: "在操作即將改變外部系統之前，於最接近副作用的位置執行政策。",
        controls: ["嚴格 Tool Schema 與 operation scope", "金額、目的地、時間與環境限制", "Idempotency、approval 與 compensation reference"],
        output: "有限度 action authorization 與結果證據"
      },
      {
        title: "Agent Interaction Gateway",
        summary: "當 Agent 溝通、fan-out 或跨越組織邊界時，維持委派工作的能力邊界。",
        controls: ["Peer identity、endpoint 與 capability 驗證", "Delegated scope、hop、retry 與 token budget", "Recursion、fan-out 與跨邊界傳輸政策"],
        output: "保留 delegation context 的可追蹤 Agent task"
      },
      {
        title: "Policy Decision Point",
        summary: "使用版本化政策評估正規化事實，讓所有 Gateway 得到一致的治理決策。",
        controls: ["Allow、Deny、RequireApproval 或 obligations", "確定性的 Decision ID 與決策說明", "受測試的 policy bundle、限時例外與緊急封鎖"],
        output: "版本化且可解釋的 Policy Decision"
      },
      {
        title: "Approval Service",
        summary: "當政策識別出高影響或例外操作時，引入可問責的人員判斷。",
        controls: ["Requester、approver 與職責分離檢查", "核准者看到的證據與 decision expiration", "綁定精確 action digest 的一次性核准"],
        output: "不可重播的 approval authorization 與決策紀錄"
      },
      {
        title: "Evaluation & Release Gate",
        summary: "只有軟體保證與 AI 特有評測都通過，才能發布精確版本的系統組合。",
        controls: ["正確性、資安、隱私與 Agent 行為測試", "Model、prompt、tool、data 與 policy 版本綁定", "Threshold、rollback 與 kill-switch 演練"],
        output: "綁定精確版本的簽章 Release Authorization"
      },
      {
        title: "Audit Evidence Plane",
        summary: "完整重建重要 AI execution，同時避免把一般 operational log 變成敏感內容倉庫。",
        controls: ["結構化 identity、delegation 與 decision event", "Metadata-first evidence 與受保護的敏感證據庫", "完整性、retention、存取稽核與 evidence package"],
        output: "兼顧隱私且可偵測竄改的 Execution Record"
      }
    ]
  }
};

const documentElement = document.querySelector("#document");
const tocElement = document.querySelector("#table-of-contents");
const sidebar = document.querySelector("#sidebar");
const backdrop = document.querySelector("#sidebar-backdrop");
const openButton = document.querySelector("#nav-open");
const closeButton = document.querySelector("#nav-close");

document.documentElement.lang = locale;
document.title = copy[locale].title;
document.querySelector("#brand-subtitle").textContent = copy[locale].subtitle;
document.querySelector("#contents-label").textContent = copy[locale].contents;
document.querySelector("#mobile-contents-label").textContent = copy[locale].contents;
document.querySelector("#footer-copy").textContent = copy[locale].footer;
document.querySelector("#loading-state span:last-child").textContent = copy[locale].loading;
document.querySelector(`[data-language="${locale}"]`)?.setAttribute("aria-current", "page");

marked.setOptions({
  gfm: true,
  breaks: false
});

function slugify(value) {
  return value
    .trim()
    .toLowerCase()
    .replace(/[\s/]+/g, "-")
    .replace(/[^\p{Letter}\p{Number}\-_]/gu, "")
    .replace(/-+/g, "-")
    .replace(/^-|-$/g, "");
}

function assignHeadingIds() {
  const used = new Map();
  documentElement.querySelectorAll("h1, h2, h3").forEach((heading) => {
    const base = slugify(heading.textContent) || "section";
    const count = used.get(base) ?? 0;
    used.set(base, count + 1);
    heading.id = count === 0 ? base : `${base}-${count + 1}`;
    heading.tabIndex = -1;
  });
}

function normalizeDocumentLinks() {
  documentElement.querySelectorAll("a[href]").forEach((link) => {
    const href = link.getAttribute("href");
    if (href === "README.md") link.setAttribute("href", "/");
    if (href === "README.zh-TW.md") link.setAttribute("href", "/zh-tw");
    if (href?.startsWith("http")) {
      link.setAttribute("target", "_blank");
      link.setAttribute("rel", "noopener noreferrer");
    }
  });
}

function buildTableOfContents() {
  const fragment = document.createDocumentFragment();
  documentElement.querySelectorAll("h2, h3").forEach((heading) => {
    const link = document.createElement("a");
    link.href = `#${heading.id}`;
    link.textContent = heading.textContent;
    link.className = heading.tagName === "H3" ? "toc-level-3" : "toc-level-2";
    link.addEventListener("click", closeNavigation);
    fragment.appendChild(link);
  });
  tocElement.replaceChildren(fragment);
}

function prepareMermaidBlocks() {
  documentElement.querySelectorAll("pre > code.language-mermaid").forEach((code) => {
    const diagram = document.createElement("div");
    diagram.className = "mermaid";
    diagram.textContent = code.textContent;
    code.parentElement.replaceWith(diagram);
  });
}

async function initializeArchitectureWalkthrough() {
  const sourceImage = documentElement.querySelector('img[src$="trust-fabric-components.svg"]');
  if (!sourceImage) return;

  try {
    const response = await fetch(sourceImage.getAttribute("src"));
    if (!response.ok) throw new Error(`HTTP ${response.status}`);

    const svgDocument = new DOMParser().parseFromString(await response.text(), "image/svg+xml");
    if (svgDocument.querySelector("parsererror")) throw new Error("Invalid architecture SVG");

    const svg = document.importNode(svgDocument.documentElement, true);
    svg.querySelector("style")?.remove();
    svg.classList.add("trust-architecture", "interactive");

    const labels = walkthroughCopy[locale];
    const walkthrough = document.createElement("section");
    walkthrough.className = "architecture-walkthrough";
    walkthrough.setAttribute("aria-label", labels.label);
    walkthrough.innerHTML = `
      <div class="walkthrough-canvas">
        <button class="walkthrough-fullscreen" type="button">
          <span class="walkthrough-fullscreen-icon" aria-hidden="true">⛶</span>
          <span class="walkthrough-fullscreen-label"></span>
        </button>
      </div>
      <div class="walkthrough-detail" aria-live="polite">
        <div class="walkthrough-identity">
          <span class="walkthrough-step"></span>
          <div class="walkthrough-title" role="heading" aria-level="3"></div>
          <p class="walkthrough-summary"></p>
        </div>
        <div class="walkthrough-control-summary">
          <strong class="walkthrough-label"></strong>
          <ul class="walkthrough-control-list"></ul>
        </div>
        <div class="walkthrough-output">
          <strong class="walkthrough-output-label"></strong>
          <span class="walkthrough-output-value"></span>
        </div>
      </div>
      <div class="walkthrough-navigation">
        <button class="walkthrough-button walkthrough-previous" type="button"></button>
        <div class="walkthrough-dots" role="group" aria-label="${labels.label}"></div>
        <button class="walkthrough-button walkthrough-next" type="button"></button>
      </div>`;

    walkthrough.querySelector(".walkthrough-canvas").appendChild(svg);
    sourceImage.replaceWith(walkthrough);

    const components = [...svg.querySelectorAll("[data-step]")]
      .sort((left, right) => Number(left.dataset.step) - Number(right.dataset.step));
    const stepLabel = walkthrough.querySelector(".walkthrough-step");
    const title = walkthrough.querySelector(".walkthrough-title");
    const summary = walkthrough.querySelector(".walkthrough-summary");
    const controlsLabel = walkthrough.querySelector(".walkthrough-label");
    const controls = walkthrough.querySelector(".walkthrough-control-list");
    const outputLabel = walkthrough.querySelector(".walkthrough-output-label");
    const output = walkthrough.querySelector(".walkthrough-output-value");
    const previous = walkthrough.querySelector(".walkthrough-previous");
    const next = walkthrough.querySelector(".walkthrough-next");
    const dotContainer = walkthrough.querySelector(".walkthrough-dots");
    const fullscreen = walkthrough.querySelector(".walkthrough-fullscreen");
    const fullscreenIcon = walkthrough.querySelector(".walkthrough-fullscreen-icon");
    const fullscreenLabel = walkthrough.querySelector(".walkthrough-fullscreen-label");

    controlsLabel.textContent = labels.controls;
    outputLabel.textContent = labels.output;
    previous.textContent = `← ${labels.previous}`;

    const dots = labels.steps.map((step, index) => {
      const dot = document.createElement("button");
      dot.type = "button";
      dot.className = "walkthrough-dot";
      dot.textContent = String(index + 1);
      dot.setAttribute("aria-label", `${labels.step} ${index + 1}: ${step.title}`);
      dot.addEventListener("click", () => selectStep(index));
      dotContainer.appendChild(dot);
      return dot;
    });

    let activeIndex = 0;

    function selectStep(index) {
      activeIndex = Math.max(0, Math.min(index, labels.steps.length - 1));
      const step = labels.steps[activeIndex];

      components.forEach((component, componentIndex) => {
        component.classList.toggle("is-active", componentIndex === activeIndex);
        component.classList.toggle("is-complete", componentIndex < activeIndex);
        component.setAttribute("aria-pressed", String(componentIndex === activeIndex));
      });
      dots.forEach((dot, dotIndex) => {
        if (dotIndex === activeIndex) dot.setAttribute("aria-current", "step");
        else dot.removeAttribute("aria-current");
      });

      stepLabel.textContent = `${labels.step} ${activeIndex + 1} ${labels.of} ${labels.steps.length}`;
      title.textContent = step.title;
      summary.textContent = step.summary;
      controls.replaceChildren(...step.controls.map((item) => {
        const listItem = document.createElement("li");
        listItem.textContent = item;
        return listItem;
      }));
      output.textContent = step.output;
      previous.disabled = activeIndex === 0;
      next.textContent = activeIndex === labels.steps.length - 1
        ? `${labels.restart} ↺`
        : `${labels.next} →`;
    }

    components.forEach((component, index) => {
      component.tabIndex = 0;
      component.setAttribute("role", "button");
      component.setAttribute("aria-label", `${labels.step} ${index + 1}: ${labels.steps[index].title}`);
      component.addEventListener("click", () => selectStep(index));
      component.addEventListener("keydown", (event) => {
        if (event.key === "Enter" || event.key === " ") {
          event.preventDefault();
          selectStep(index);
        }
      });
    });

    previous.addEventListener("click", () => selectStep(activeIndex - 1));
    next.addEventListener("click", () => selectStep(
      activeIndex === labels.steps.length - 1 ? 0 : activeIndex + 1
    ));

    function updateFullscreenButton() {
      const isFullscreen = document.fullscreenElement === walkthrough;
      walkthrough.classList.toggle("is-fullscreen", isFullscreen);
      fullscreenIcon.textContent = isFullscreen ? "×" : "⛶";
      fullscreenLabel.textContent = isFullscreen ? labels.exitFullscreen : labels.fullscreen;
      fullscreen.setAttribute("aria-label", isFullscreen ? labels.exitFullscreen : labels.fullscreen);
    }

    if (walkthrough.requestFullscreen && document.fullscreenEnabled) {
      fullscreen.addEventListener("click", async () => {
        try {
          if (document.fullscreenElement === walkthrough) await document.exitFullscreen();
          else await walkthrough.requestFullscreen();
        } catch (error) {
          console.error("Fullscreen mode could not be changed.", error);
        }
      });
      document.addEventListener("fullscreenchange", updateFullscreenButton);
      updateFullscreenButton();
    } else {
      fullscreen.hidden = true;
    }

    selectStep(0);
  } catch (error) {
    console.error("Architecture walkthrough could not be initialized.", error);
  }
}

function observeSections() {
  const links = new Map(
    [...tocElement.querySelectorAll("a")].map((link) => [link.getAttribute("href").slice(1), link])
  );
  const observer = new IntersectionObserver((entries) => {
    const visible = entries
      .filter((entry) => entry.isIntersecting)
      .sort((left, right) => left.boundingClientRect.top - right.boundingClientRect.top)[0];
    if (!visible) return;
    links.forEach((link) => link.removeAttribute("aria-current"));
    links.get(visible.target.id)?.setAttribute("aria-current", "location");
  }, { rootMargin: "-88px 0px -70% 0px" });
  documentElement.querySelectorAll("h2, h3").forEach((heading) => observer.observe(heading));
}

function openNavigation() {
  sidebar.classList.add("is-open");
  backdrop.hidden = false;
  openButton.setAttribute("aria-expanded", "true");
}

function closeNavigation() {
  sidebar.classList.remove("is-open");
  backdrop.hidden = true;
  openButton.setAttribute("aria-expanded", "false");
}

openButton.addEventListener("click", openNavigation);
closeButton.addEventListener("click", closeNavigation);
backdrop.addEventListener("click", closeNavigation);
window.addEventListener("keydown", (event) => {
  if (event.key === "Escape") closeNavigation();
});

try {
  const response = await fetch(contentPath, { cache: "no-cache" });
  if (!response.ok) throw new Error(`HTTP ${response.status}`);
  const markdown = await response.text();
  documentElement.innerHTML = marked.parse(markdown);
  assignHeadingIds();
  normalizeDocumentLinks();
  await initializeArchitectureWalkthrough();
  prepareMermaidBlocks();
  buildTableOfContents();
  observeSections();

  mermaid.initialize({
    startOnLoad: false,
    securityLevel: "strict",
    theme: "base",
    themeVariables: {
      primaryColor: "#ecfdf5",
      primaryTextColor: "#172033",
      primaryBorderColor: "#0f766e",
      lineColor: "#64748b",
      secondaryColor: "#f8fafc",
      tertiaryColor: "#ffffff",
      fontFamily: "Inter, ui-sans-serif, system-ui, sans-serif"
    },
    flowchart: { curve: "basis", htmlLabels: true }
  });
  await mermaid.run({ querySelector: ".mermaid" });
} catch (error) {
  console.error(error);
  documentElement.innerHTML = `<div class="error-state"><strong>${copy[locale].error}</strong></div>`;
}
