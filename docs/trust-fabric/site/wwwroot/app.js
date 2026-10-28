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
