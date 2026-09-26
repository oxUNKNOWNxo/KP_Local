# KoishiPro2 iOS / WindBot プロジェクト状態

最終更新: 2026-09-26  
対象リポジトリ: `oxUNKNOWNxo/KP_Local`  
対象ブランチ: `main`

> **次のチャットで最初に読むこと。**
> このファイルは、KoishiPro2 iOS版のローカルWindBot統合、iPhone X画面対応、超先行カード用 `expansions` 対応について、現在の正常基準と過去の失敗経路を記録する引き継ぎ文書。
> 新しいチャットでは、実装を変更する前にこのファイルと記載された主要ファイルを確認し、既に解決済みの方式へ逆戻りしないこと。
>
> **KoishiPro2本体・WindBot・ocgcore・script・Unityの更新を行う場合は、作業前に必ず `UPSTREAM_UPDATE.md` も読むこと。**
> 上流更新の手順、衝突しやすい箇所、削除候補patch、必須CI、実機回帰項目はそちらを正とする。

---

## 1. 現在の正常基準

2026-09-26 時点で、ユーザー実機にて以下を確認済み。

- **AI対戦が正常に開始・進行する**
- 相手AIとして WindBot / Radiant Typhoon が正常稼働する
- **`expansions/*.cdb` にのみ存在する追加カードが正常なカード種別で扱われる**
- **`expansions/script/c<ID>.lua` に置いた追加カードLuaが正常に利用される**
- 既存カードの標準Luaは、`expansions/script` によって不用意に上書きされない
- 画面の縦横比は実機で想定どおり
- iPhone X横画面のノッチ側余白は、左右方向・余分な隙間ともに実機で解消確認済み
- AI選択画面の不要な空行問題は修正済み

正常基準の主なCI:

- **KoishiPro2 iOS cloud build #106: success**
  - run ID: `36147091332`
  - head: `afc519d97aa2255db0a682c5c45997f51b7974dc`
- **KoishiPro2 WindBot integration validation #59: success**
  - run ID: `36147091345`
- **WindBot local duel smoke test #15: success**
  - run ID: `36127534383`

実機でも build #106 相当で **AI対戦および追加カードの正常動作を確認済み**。
今後、不具合が出た場合はまずこの状態との差分を見ること。

---

## 2. プロジェクトの目的

KoishiPro2のiOS版をTrollStore環境で利用しつつ、以下を成立させる。

1. 外部サーバーに依存せず、端末内ocgcore + WindBotでAI対戦する
2. WindBotは現在のRadiant Typhoon AIを利用する
3. KoishiPro2本体の操作感・画面構成を壊さない
4. 超先行カードを、公式側やバンドル済みスクリプト更新前でも利用可能にする
5. 追加カードは通常の `expansions` 運用に近い形で管理できるようにする
6. GitHub Actionsで再現可能なiOS IPAビルドを維持する

---

## 3. 超先行カードの現在仕様【重要】

### 3.1 CDB

追加カード情報はユーザーが通常どおり

```
expansions/
  example.cdb
```

のように配置する。

**重要: WindBot側で `expansions/*.cdb` を再読込してはいけない。**

一度この方式を実装したが、実機でAI対戦開始時に

```
An item with the same key has already been added. Key: 572850
```

が発生した。

原因は、KoishiPro2本体ですでに統合されているカードを、WindBot側の独立辞書にも追加CDBから再登録しようとしたため。

### 3.2 現在の正しいカードデータ経路

WindBotのバンドル済み `cards.cdb` は**独立したまま保持する**。

ocgcoreがカードデータを要求した場合:

```
WindBot側の標準 cards.cdb にカードがある
    ↓ YES
標準WindBotカードデータを使用

    ↓ NO

KoishiPro2本体の YGOSharp.CardsManager に問い合わせる
    ↓
本体が expansions/*.cdb から既に読み込んだカード情報を使用
```

この方式により、

- 標準WindBotカードDBを汚さない
- 同一IDの二重登録を避ける
- 超先行カードのみ本体側から補完できる

という状態になっている。

### 3.3 Luaスクリプト

追加Luaは

```
expansions/script/c12345678.lua
```

形式。

ocgcoreが

```
./script/c12345678.lua
```

を要求した際のルール:

1. まず通常のバンドル済み標準スクリプトを探す
2. 標準側に存在する場合はそれを使う
3. **標準側に存在しないカードスクリプトの場合のみ**
   `expansions/script/c<ID>.lua` を探す
4. 見つかればそれを使用

つまり `expansions/script` は「既存カードLuaのMOD上書き機構」ではなく、
**まだ標準スクリプトに入っていない超先行カードを補完するための機構**として扱う。

`utility.lua`, `procedure.lua`, `constant.lua` などの共通スクリプトを
`expansions/script` から任意に差し替える設計にはしない。

---

## 4. AI対戦の現在構成

ローカルAI対戦は以下の構成。

```
KoishiPro2 UI
    ↓
KoishiWindBotBridge
    ↓
RadiantTyphoonLocalDuel
    ↓
LocalDuelRouter
    ↓
LocalDuelNative
    ↓
iOS内蔵 koishi_ocgcore
    ↕
WindBot managed runtime
```

サーバー対戦ではなく、端末内で完結する。

主要ファイル:

- `patches/ai/windbot/KoishiWindBotBridge.cs`
  - KoishiPro2本体とローカルWindBotの接続
  - 本体側カードデータのフォールバック供給
- `windbot/runtime/RadiantTyphoonLocalDuel.cs`
  - ローカルデュエル組み立て
  - WindBot標準カードDB → 本体カードデータのフォールバック
- `windbot/runtime/LocalDuelNative.cs`
  - ocgcore P/Invoke
  - カードデータreader
  - Lua script reader
  - `expansions/script` フォールバック
- `windbot/runtime/LocalDuelRouter.cs`
  - ocgcoreメッセージとWindBot/人間クライアント間のルーティング
- `windbot/prepare_windbot_subset.sh`
  - iOS向けWindBot managed subset生成
- `patches/prepare_windbot_runtime.sh`
  - KoishiPro2へWindBotランタイムを配置

---

## 5. 過去に重要だったAI修正

### Radiant Typhoon Chant

カード:
- 絢嵐たる献詠 / Radiant Typhoon Chant
- ID: `67115133`

以前、AIがこのカードを使用した際に進行停止が起きていた。

現在はocgcoreのメッセージ境界・`MSG_SET` payload処理などを現行coreに合わせて修正し、
AIターン完走スモークが成功している。

関連コミット:

- `d45d4f9465ecbf43e13b3b52ad92283b7ed6da9a`
  - process loopを現行Koishi coreに合わせる
- `69e3b3f1c70c648eeb54f67039abe78edf558d52`
  - router message boundary trace
- `36efacba7079090f660001f0b68b23353e79e8ea`
  - current core `MSG_SET` payload対応

**これらの処理を古いYGOPro/WindBotプロトコルへ戻さないこと。**

---

## 6. 追加カード対応の経緯と禁止事項

### 最初のLua対応

コミット:
- `718569735e446ab4495b8aee970cb8f9e5684658`
  - 標準に無いカードLuaを `expansions/script` から補完

これは現在も有効な方向性。

### 一度採用して失敗したCDB方式

コミット:
- `63e4520f27cd0459bea7e7966add0f21f2d02aa9`
  - WindBotが `expansions/*.cdb` を直接再読込する方式

この方式は最終的に**撤回**。

実機で重複キー例外:
```
An item with the same key has already been added. Key: 572850
```

が発生したため、今後復活させない。

### 現在の方式

関連コミット:

- `1a69596d36d009134b938909054dae49117000da`
  - expansion-only card用にhost card provider導入
- `4ee070094c38dc6af9c0f2298f4b9c2244cdf36f`
  - Koishi本体カード情報をlocal ocgcoreへ供給
- `a2cd01fe6953b0334693b172e4a93365ca66dee4`
  - WindBotカードDBの独立性を復元
- `17f9cbe5c50129a1883449f366c3d7ff84a311c6`
  - host fallback + expansion script fallbackのスモーク
- `afc519d97aa2255db0a682c5c45997f51b7974dc`
  - Koishi固定ソースとの型差異を修正

`afc519...` では以下を修正:

```csharp
Setcode = unchecked((ulong)card.Setcode),
RuleCode = 0
```

固定KoishiPro2ソース側の `YGOSharp.Card` は
WindBot側カード型と完全一致しないため、型を混同しないこと。

---

## 7. iPhone X 画面対応

対象:
- `patches/prepare_notch_compat.py`

目的:
- 古い16:9互換キャンバスではなく、iPhone X実画面幅を活用
- ノッチ側のみ余白を確保
- 反対側は物理画面端まで利用

過去の問題:
- 横向き時、余白を作る左右が逆だった

修正:
- LandscapeLeft / LandscapeRight の扱いを実機挙動に合わせて反転

関連コミット:
- `ba99f6691bd7bfe8c75034f1f65c375fea677905`

さらに、safeAreaInsetsの44ptをそのまま使うと
ノッチとゲーム画面の間にわずかに余分な隙間が見えたため、
現在は概ね以下の補正を入れている。

```
cutout = MAX(30.0, cutout - 12.0)
```

関連コミット:
- `3a48e28395d39a3e2e802c47c16e4a2fd738c767`

ユーザー実機で**縦横比は正常確認済み**。
ノッチ余白についてさらに微調整する場合も、
まず現在値との差分で行い、旧16:9固定方式へ戻さないこと。

---

## 8. AIデッキ一覧の空行

AI選択画面に、先頭に空のデッキ項目が見える問題があった。

現在は `AIRoom.cs` 側で

- prototype rowを画面外へ移動
- 空文字デッキ名を除外
- 重複名を除外

している。

この修正は正常動作確認済みなので、
UI一覧ロジックを触る場合は再発に注意。

---

## 9. CI / テスト

主要workflow:

- `.github/workflows/ios-cloud-build.yml`
  - 実際のiOS IPAビルド
  - Unity認証が必要な場合あり
- `.github/workflows/ai-integration-validate.yml`
  - KoishiPro2へWindBot統合後の静的/ネイティブ検証
- `.github/workflows/windbot-csharp-validate.yml`
  - WindBot subsetのC#コンパイル検証
- `.github/workflows/windbot-duel-smoke.yml`
  - host ocgcore + WindBotの実デュエルスモーク

### expansion-onlyカードのスモーク

スモークではテスト専用ID `19999999` を使い、

- 標準 `cards.cdb` には存在しない
- host側カードデータfallbackでモンスター情報を供給
- `expansions/script/c19999999.lua` を補完
- AIターンを完走

まで確認する。

実運用のユーザーCDB自体をCIに保存するわけではない。

---

## 10. Unityビルド時の注意

Unity CLI認証が必要な場合、
GitHub Actionsの `Sign in to Unity account` でワンタイムURLが表示される。

ユーザーはChatGPTアプリ版を利用する場合があるため、
作業中にUnity認証ステップへ入ったらチャット内で明示的に通知する。

### 過去の #105 コンパイルエラー

build #105では以下が発生:

```
KoishiWindBotBridge.cs(...): error CS0266
Cannot implicitly convert type 'long' to 'ulong'

KoishiWindBotBridge.cs(...): error CS1061
'Card' does not contain a definition for 'RuleCode'
```

これを `afc519...` で修正し、
**build #106 は成功**。

また、`ios-cloud-build.yml` は今後
Unity失敗時に `error CSxxxx` をログへ直接出すよう修正済み。

---

## 11. 固定KoishiPro2ソース

iOS cloud buildで利用しているKoishiPro2ソース:

- repository: `https://code.moenext.com/hex/ygopro2.git`
- commit: `c1850e1120df6a5885692eadacad8ad114a3cf11`

この固定ソースの型・APIを基準にiOS統合コードを書くこと。

「現在の別フォークでは存在するプロパティ」が
この固定コミットにも存在するとは限らない。
今回の `RuleCode` 問題がその例。

---

## 12. 次のチャットでの作業開始手順

新しいチャットでこのプロジェクトを継続する場合:

1. この `PROJECT_STATE.md` を最初に読む
2. **上流更新を伴う作業なら `UPSTREAM_UPDATE.md` を続けて読む**
3. `main` の最新commitと最新成功CIを確認
4. 正常基準は build #106 / commit `afc519...` 以降
5. 追加カード問題の場合は、
   **WindBot側でCDBを再読込しない**ことを最初に確認
6. Lua問題の場合は、
   「標準Luaが無い時だけ `expansions/script/c<ID>.lua`」
   という現在仕様を維持
7. AI進行停止の場合は、
   `LocalDuelRouter.cs` と current-core message parsing の回帰を疑う
8. iOSビルドエラーの場合は、
   GitHub ActionsのUnity compiler error行を直接確認
9. 実機で正常だった既存機能を壊さないよう、
   修正はなるべく局所的に行う
10. 修正後は最低でも
   C# validation / integration validation / duel smoke
   を確認してからiOS実機版へ進む

---

## 13. 現時点の結論

このプロジェクトは現在、

- KoishiPro2 iOS
- 内蔵ocgcore
- ローカルWindBot
- Radiant Typhoon AI
- `expansions/*.cdb` の超先行カード
- `expansions/script/c<ID>.lua` の超先行カード効果

を同時に利用できる状態まで到達している。

**ユーザー実機でAI対戦と追加カードの正常動作を確認済み。**

今後の変更では、この状態を「動作基準」として扱うこと。


---

## 14. WindBot AI多デッキ化

2026-09-26時点で、Radiant Typhoon固定から複数AI対応へ拡張。

- upstream WindBotの `[Deck(...)]` と対応YDKをビルド時に解析
- IL2CPP対策としてReflection/Activator.CreateInstanceには戻さず、直接 `new XxxExecutor(...)` するregistryを自動生成
- 対応YDKが存在するAIを登録
- 検証時点では72デッキを登録
- `OldSchool` はupstreamに対応YDKが無いため除外
- Blue-Eyesを代表としてRadiant以外のExecutor生成をCIで確認

AI対戦画面は3カラム構成。

```
[ 自分のデッキ一覧 ] [ オプション ] [ AIデッキ一覧 ]
```

- 左: 既存 `UIselectableList` を使った自分のデッキ一覧
- 右: 左一覧をruntime複製した独立 `UIselectableList` でAIデッキ72件を全件スクロール
- 中央:
  - 旧 `rank_` / `aideck_` Popupは完全非表示
  - 選択中デッキ名は中央へ重複表示しない。長いデッキ名が左右一覧へ侵入するため、選択状態は各一覧のハイライトだけで示す
  - `unrand_` = 「シャッフルしない」
  - `first_` = 「自分が先攻」
  - 対戦開始 / 戻る
- 自分のデッキ選択は `deckInUse` Configへ保持
- AIデッキ選択は `list_aideck` Configへ保持
- リストの行文字は22
- 中央オプション/表示/ボタン文字は22で統一
- 左右一覧には「自分のデッキ」「AIデッキ」の見出しを表示
- 旧ページ式・一覧切替式は廃止

実装では既存 `trans_AIroom.prefab` の `deck` オブジェクトを複製する。
同オブジェクト配下に `panel_` と `bar_` が含まれているため、
スクロール領域・スクロールバーは左右で独立する。

### 重要: AIルーム親UIPanelのclip

#120/#124の実機確認で、`mainWindow` を980幅へ広げても左右リストが中央の旧500幅で切られる問題を確認した。
原因は `mainWindow` の親 `GameObject` に付く `UIPanel` の
`mClipRange = {x:0,y:0,z:500,w:400}`。

したがって3カラム化では、`mainWindow` Spriteだけでなく次も必ず同時に変更する。

- 親 `UIPanel.baseClipRegion`: 1100x480
- `mainWindow`: 1100x480
- `glass` 背景: 1064x430
- deck list frame: 330
- deck list clip: 290
- scrollbar x: 160
- header separator: 1068

`patches/patch_ai_room_prefab.py` でPrefabのシリアライズ値自体を変更し、
`AIRoom.ConfigureMainWindow()` / `ApplyStableLayout()` でも表示直後に再適用する。
親UIPanelのclip更新を省くと、子要素の座標だけ動いて見た目が旧幅のままになる。


### 重要: 旧Popupを中央表示へ流用しない

#126相当の実機画面で、`rank_` / `aideck_` の `content_` ラベルを中央表示へ流用すると、
Popup内部のAnchor/子ラベル位置が残り、選択中デッキ名が左側デッキ一覧へめり込むことを確認。

したがって今後は:

- `rank_` / `aideck_` は完全非表示
- 選択中デッキ名は中央へ表示しない。左右一覧の選択ハイライトを正とする
- 左右一覧は `x=±310` に配置
- 各一覧frameは330幅、clipは290幅
- 「自分のデッキ」「AIデッキ」の見出しは各一覧と同じ `x=±310` を中心にし、`pivot=Center` / `alignment=Center` を明示してテンプレート由来のずれを残さない
- ウィンドウは1100幅とし、左右一覧の外側に約75pxの余白を確保
- 中央カラムにはチェック項目2個と開始/戻るだけを置く。開始/戻るは250幅
- `percyHint` 複製は実機では描画されないため見出しのテンプレートに使わない
- standalone UILabelは、実機で表示確認済みのオプションUILabelを複製して作る
- ウィンドウ高は480、glassは430として、左右リスト最下段と「戻る」の下に余白を残す

とする。旧Popupの位置補正や `percyHint` 複製で表示を作る方式へ戻さない。

## 15. デッキシャッフル

AI対戦UIの `unrand_` は **「シャッフルしない」** と表示する。

旧Percy AIでは対戦開始前にプレイヤーのMain DeckをC#側で明示シャッフルしていたが、
WindBot移行時にこの処理が抜け、ON/OFFで初期デッキ順が変わらない回帰が発生した。

現在は:

- OFF: プレイヤーMain Deckを対戦開始前にFisher-Yatesで明示シャッフルし、ocgcore通常シャッフルも許可
- ON: YDK順を維持し、`DUEL_PSEUDO_SHUFFLE (0x10)` も設定
- AI側Main Deckは旧実装と同様、対戦開始時に常にシャッフル
- Extra Deckはシャッフルしない

ローカルduel smokeで「順序維持」と「明示シャッフル」の両経路を回帰テストする。

## 16. expansion Luaフォルダ名

追加カードLuaの正式な配置先は今後

```
expansions/script/c<ID>.lua
```

とする。

旧 `expansions/scripts` は使用しない。
標準 `script/c<ID>.lua` が存在しないカードだけ、`expansions/script/c<ID>.lua` をfallbackとして読む。


---

## 17. aliasとカードスクリプト選択

2026-09-26、ユーザー実機確認で、CDBの `alias` を持つ原作寄り改変カードがalias先のOCG版Luaを使う問題を確認。

旧Koishi/purerosefallen coreでは:

```
get_original_code() = alias ? alias : code
register_card() -> load_card_script(get_original_code())
```

だったため、aliasが遠い別IDでも無条件でalias先 `c<alias>.lua` を使用していた。

現在は現行EDOProの挙動に合わせる。

- `alias` と本人 `code` が±10未満の近接ID:
  - alternate printingとしてalias先スクリプトを使用
- それ以外の遠いalias:
  - 本人 `code` のスクリプトを使用
  - 追加カードなら `expansions/script/c<code>.lua` へfallback

alias値自体は0に潰さない。カード名・同名判定などalias本来の意味は保持し、スクリプト選択だけを互換修正する。

実装:
- `windbot/patch_koishi_core_alias_script.py`
- iOS core buildとhost duel smokeの両方で同じpatchを適用

回帰テストでは、遠いaliasを持つ expansion-only カードにも本人IDのLuaを用意し、
ログが `expansions/script/c<本人ID>.lua` を読むことを確認する。


---

## 18. 上流更新時の参照文書

KoishiPro2本体、WindBot、ocgcore、script、Unityの更新・追従作業を行う場合は、
**実装を変更する前に `UPSTREAM_UPDATE.md` を必ず読むこと。**

`PROJECT_STATE.md` は「現在何が正常か・なぜ現在方式になったか」を記録し、
`UPSTREAM_UPDATE.md` は「新しい上流へどう安全に移行するか」を記録する。

上流更新時は両方をセットで参照する。


---

## 19. AI対戦UI簡素化と先後決定

旧KoishiPro2/Percy用AI画面のうち、WindBot版では不要・無効だった項目を整理。

非表示:

- `life_` — 初期ライフ設定。WindBotローカル対戦は8000固定
- `mr4_` — 旧「新マスタールール」切替。現在はMaster Rule 2020 / rule 5固定
- `god_` — 旧Percyの相手非公開情報表示モード。WindBot版では使用しない

中央に残す:

- `unrand_` — 「シャッフルしない」
- `first_` — 「自分が先攻」
- 対戦開始 / 戻る

選択中の自分デッキ・AIデッキ名は中央へ重複表示しない。長い名称で左右一覧へ被ることを防ぎ、選択状態は左右リストのハイライトで確認する。

`first_` がONならプレイヤー先攻で即開始。
OFF時は画面上に補足文を出さず、KoishiPro2が通常対戦で使用している
`RMSshow_tp` のグー/チョキ/パーUIと `new_ui_handShower` の結果表示を再利用してWindBotとじゃんけんする。

- WindBotの手は選択中Executorの `OnRockPaperScissors()`
- プレイヤー勝利時は既存の先攻/後攻選択UIを表示
- WindBot勝利時は同Executorの `OnSelectHand()` でAIが先攻/後攻を決定
- あいこは再じゃんけん
- じゃんけん中はAIRoomの子UIを非表示にし、AI選択画面を背景へ残さない
- ネット対戦用 `TcpHelper.CtosMessage_HandResult` は使わず、ローカルAI内で完結

じゃんけんUIの元実装は固定KoishiPro2 `Assets/SibylSystem/Room/Room.cs`。
上流更新時は `RMSshow_tp`, `RMSshow_FS`, `new_ui_handShower`, `Program.go` の互換性を確認する。


---

## 20. AI対戦UI位置基準（2026-09-27）

実機画面で、中央の選択中デッキ名が長い場合にAI一覧へ重なり、左右の見出しもテンプレート由来のpivot/alignmentによって一覧中央からずれることを確認した。

現在の基準:

- 中央の「自分: <デッキ名>」「AI: <デッキ名>」表示は廃止
- 選択デッキは左右一覧のハイライトで確認する
- 「自分のデッキ」「AIデッキ」の見出しは一覧中心 `x=±310` に固定
- runtime生成する見出しUILabelは `UIWidget.Pivot.Center` / `NGUIText.Alignment.Center` を明示
- 中央は「シャッフルしない」「自分が先攻」「対戦開始」「戻る」の4項目だけにする
- 中央4項目は、選択デッキ表示撤去後の空間を使って従来より上へ寄せる

今後は見た目に合わせて各要素を個別に数pxずつ追いかけるのではなく、左右一覧中心・画面中心を基準座標として調整する。
