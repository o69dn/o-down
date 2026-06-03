// o-down Chrome / Edge extension (MV3)
// Sends a structured message to the registered native messaging host.
const HOST_NAME = "o_down_native_messaging";

chrome.runtime.onInstalled.addListener(() => {
  chrome.contextMenus.create({
    id: "send-to-odown",
    title: "Download with o-down",
    contexts: ["link", "selection", "page"]
  });
  chrome.contextMenus.create({
    id: "send-all-to-odown",
    title: "Download all links on page with o-down",
    contexts: ["page"]
  });
});

chrome.contextMenus.onClicked.addListener(async (info, tab) => {
  let urls = [];
  if (info.menuItemId === "send-to-odown") {
    const url = info.linkUrl || info.selectionText || info.pageUrl;
    if (url) urls.push(url);
  } else if (info.menuItemId === "send-all-to-odown") {
    try {
      const [{ result }] = await chrome.scripting.executeScript({
        target: { tabId: tab.id },
        func: () => Array.from(document.querySelectorAll("a[href]")).map(a => a.href)
      });
      urls = result || [];
    } catch (e) { console.error("o-down: extract failed", e); }
  }

  for (const u of urls) {
    try {
      await chrome.runtime.sendNativeMessage(HOST_NAME, {
        url: u,
        referrer: info.pageUrl,
        filenameHint: guessFilename(u),
        source: "chrome-extension"
      });
    } catch (e) {
      console.error("o-down: send failed", e);
    }
  }
});

chrome.action.onClicked.addListener(async (tab) => {
  if (!tab || !tab.url) return;
  await chrome.runtime.sendNativeMessage(HOST_NAME, {
    url: tab.url,
    referrer: tab.url,
    source: "chrome-action"
  });
});

function guessFilename(u) {
  try { return new URL(u).pathname.split("/").pop() || null; }
  catch { return null; }
}
