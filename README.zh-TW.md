<p align="center">
  <img src="docs/assets/hero.svg" width="960" alt="G HUB Battery Widget 預覽">
</p>

<p align="center">
  <a href="README.md">English</a> ·
  <a href="README.zh-TW.md"><strong>繁體中文</strong></a>
</p>

<p align="center">
  <img alt="Windows 10 與 11" src="https://img.shields.io/badge/Windows-10%20%7C%2011-2384d7?logo=windows11&amp;logoColor=white">
  <img alt="MIT 授權" src="https://img.shields.io/badge/license-MIT-2dc96c">
  <img alt="無遙測" src="https://img.shields.io/badge/telemetry-none-2dc96c">
  <img alt="免安裝" src="https://img.shields.io/badge/install-portable-8b7cf6">
</p>

# G HUB Battery Widget

一個迷你的 Windows 桌面電量浮窗，直接使用 Logitech G HUB 已經取得的裝置資料。它適合只想快速查看電量、不想再安裝另一套裝置管理服務的人。

調整背景透明度時，文字與圖示會維持完整亮度；插拔 USB 後約五秒內更新充電狀態；多螢幕位置可自動保存與復原；程式不包含遙測或網路更新功能。

<p align="center">
  <img src="docs/assets/features.svg" width="960" alt="免安裝、隱私、多螢幕與透明背景功能">
</p>

## 特色

- **真正的背景透明**：提供 100%、85%、70%、55% 與背景完全透明；文字和圖示不會跟著變暗。
- **辨識充電狀態**：在 LIGHTSPEED 與 USB 之間自動切換，充電時顯示閃電與藍色電量。
- **多螢幕位置復原**：以實體螢幕座標保存位置；螢幕拔除或配置改變後，自動把浮窗搬回可見範圍。
- **免安裝**：單一小型 EXE，不安裝驅動、服務或額外 Runtime 套件。
- **隱私優先**：只連接本機 `127.0.0.1` 上的 G HUB；沒有遙測、分析或遠端網路請求。
- **適合桌面使用**：圓角卡片、拖曳定位、邊緣吸附、永遠置頂與選用的滑鼠穿透。
- **系統匣控制**：高對比電量數字、單擊顯示／隱藏、立即更新與選用的開機啟動。

<p align="center">
  <img src="docs/assets/states.svg" width="960" alt="系統匣圖示與背景透明狀態">
</p>

## 系統需求

- Windows 10 或 Windows 11
- Logitech G HUB 在背景執行
- G HUB 能正常顯示電量的 Logitech 裝置
- .NET Framework 4.8；目前的 Windows 10/11 已內建

目前已使用 **PRO X SUPERLIGHT** 實機測試。其他由 G HUB 回報電量的裝置可能也能運作，但目前尚未加入多裝置選擇功能。

## 下載與使用

1. 從最新 Release 下載 `GHubBatteryWidget.exe`。
2. 保持 Logitech G HUB 在背景執行，直接啟動 EXE，不需要安裝。
3. 按住浮窗拖曳即可移動；在浮窗或系統匣圖示上按右鍵，可調整背景透明度、置頂、滑鼠穿透、立即更新、螢幕位置、開機啟動或結束程式。
4. 開啟滑鼠穿透後，請從系統匣圖示打開選單。

第一次啟動會出現在滑鼠所在螢幕的右下角。「移到目前螢幕右下角」可以快速搬到另一個螢幕；若原本的螢幕消失，浮窗會自動回到可見的工作區域。

> 社群版本沒有商業程式碼簽章，因此 Windows SmartScreen 可能顯示「無法辨識的應用程式」。專案包含完整原始碼與可重現建置腳本，也可以自行編譯。

## 隱私與資安

程式只會：

- 連接 `ws://127.0.0.1:9010`；
- 對 `/devices/list` 與 `/battery/{deviceId}/state` 發送唯讀請求；
- 在本機 `GHubBatteryWidget.ini` 保存實體 X/Y 位置、背景透明度、置頂與滑鼠穿透狀態；
- 僅在使用者主動開啟「隨 Windows 啟動」時，把目前 EXE 的引號路徑寫入目前使用者的 Run 登錄值。

它不會上傳裝置資料、不連接遠端主機、不讀取文件、瀏覽器資料、憑證或剪貼簿，也不安裝服務或驅動、不要求系統管理員權限。

本機訊息設有六秒逾時與 1 MiB 大小限制；裝置 ID 與電量範圍會先驗證。完整信任邊界與漏洞回報方式請見 [SECURITY.md](SECURITY.md)。

## 從原始碼建置

不使用 NuGet 套件或下載的相依套件。在 Windows PowerShell 執行：

```powershell
.\build.ps1
```

腳本會使用 Windows 內建的 .NET Framework C# 編譯器，輸出至 `dist\GHubBatteryWidget.exe`。

## 協定參考與致謝

本儲存庫的程式、介面與美術均為獨立撰寫，沒有包含下列專案的原始檔或二進位元件。我們查閱了它們的公開文件與實作，以確認 G HUB 本機 WebSocket 的行為：

- [bmrussell/LGBattery](https://github.com/bmrussell/LGBattery)：記錄 `ws://localhost:9010` 連線標頭、裝置列舉與電量端點（Apache-2.0）。
- [andyvorld/LGSTrayBattery](https://github.com/andyvorld/LGSTrayBattery)：較早的開源 G HUB／HID 電量監控工具，作為行為參考（GPL-3.0）。

### 相關專案

[HeyOkay/HaloBattery](https://github.com/HeyOkay/HaloBattery) 是成熟的多品牌系統匣電量工具，可直接與 HID 裝置溝通，不需要 G HUB。若重視廣泛裝置支援與單純系統匣顯示，可以優先選擇 HaloBattery；若希望使用桌面浮窗，則可使用本專案。

Logitech、G HUB、LIGHTSPEED 與產品名稱為 Logitech 的商標。本專案為獨立作品，未受 Logitech 贊助或認可。

## 授權

採用 [MIT License](LICENSE)。
