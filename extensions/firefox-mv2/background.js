// o-down Firefox MV2 fallback
const HOST_NAME = "o_down_native_messaging";

browser.runtime.onInstalled.addListener(() => {
  browser.contextMenus.create({
    id: "send-to-odown",
    title: "Download with o-down",
    contexts: ["link", "selection", "page"]
  });
});

browser.contextMenus.onClicked.addListener(async (info) => {
  const url = info.linkUrl || info.selectionText || info.pageUrl;
  if (!url) return;
  try {
    await browser.runtime.sendNativeMessage(HOST_NAME, {
      url,
      referrer: info.pageUrl,
      source: "firefox-mv2"
    });
  } catch (e) {
    console.error("o-down: send failed", e);
  }
});
