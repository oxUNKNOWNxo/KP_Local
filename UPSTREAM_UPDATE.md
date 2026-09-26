# KoishiPro2 上流更新手順

最終更新: 2026-09-26  
対象リポジトリ: `oxUNKNOWNxo/KP_Local`  
対象ブランチ: `main`

> **上流KoishiPro2・WindBot・ocgcore・scriptの更新を行う前に必ず読むこと。**
> このリポジトリはKoishiPro2本体のforkではなく、固定した上流ソースへ独立したpatch/runtime層を適用してiOS/TrollStore版を生成する構成。
> 上流更新時は「既存改造を新ソースへ丸ごと移植」するのではなく、各patchがまだ必要かを再判定し、必要なものだけ新しい上流へ再適用する。

---

## 1. この文書の目的

現在の改変量では、KoishiPro2本体のcommitだけを新しくして即ビルドする方法は危険。

主な理由:

- `Program.cs` の起動・カードDB更新・iOS負荷制御をpatchしている
- `Menu.cs` にAIメニュー復元・配置処理を追加している
- `AIRoom.cs` はWindBot用実装へ置き換えている
- WindBot managed runtimeを生成してKoishiPro2へ追加している
- iOS用ocgcoreを別途ビルドしている
- ocgcore内部のalias→Lua選択へ互換patchを当てている
- UnityからXcodeを書き出した後にもノッチ対応のnative patchを当てている

したがって、上流更新は**機能レイヤーごとの再適合作業**として行う。

---

## 2. 現在の上流構成

### 2.1 KoishiPro2本体 — 固定

iOS build / integration validationで使用:

```
SOURCE_REPO=https://code.moenext.com/hex/ygopro2.git
SOURCE_COMMIT=c1850e1120df6a5885692eadacad8ad114a3cf11
```

定義箇所:

- `.github/workflows/ios-cloud-build.yml`
- `.github/workflows/ai-integration-validate.yml`

**KoishiPro2本体更新時は、この2箇所を同じcommitへ変更する。**
片方だけ変更してはならない。

### 2.2 Unity — 固定

現在:

```
Unity 2021.3.45f1
changeset 0da89fac8e79
```

定義:

- `.github/workflows/ios-cloud-build.yml`

Unity更新はKoishiPro2本体更新とは別作業として扱う。
特に `prepare_notch_compat.py` は生成されたXcodeソースをpatchするため、Unity側の出力構造変更の影響を受ける。

### 2.3 WindBot / ocgcore / script — build時に解決

`windbot/build_koishi_core_ios.sh` は現在:

- `purerosefallen/ygopro` master
- そのcommitが参照する `ygopro-core` revision
- そのcommitが参照する `ygopro-scripts` revision
- `purerosefallen/windbot` master

を取得する。

したがって、**KoishiPro2本体のSOURCE_COMMITを変えなくてもruntime側が更新され得る。**

生成物には:

```
koishi-runtime-revisions.txt
windbot-revision.txt
koishi-main-scripts-revision.txt
```

を残す。

不具合発生時は「KoishiPro2本体更新」と「WindBot/core/script更新」を混同せず、
まずrevisionファイルで差分を特定する。

---

## 3. 改変レイヤー

現在の構成は以下の順。

```
KoishiPro2 upstream source
        ↓
patches/apply_ios_mods.py
        ↓
patches/sync_main_scripts.sh
        ↓
patches/finalize_offline_ai.py
        ↓
patches/activate_windbot_ai.py
        ↓
patches/prepare_windbot_runtime.sh
        ↓
windbot/build_koishi_core_ios.sh
        ↓
Unity iOS import / IL2CPP / Xcode export
        ↓
patches/prepare_notch_compat.py
        ↓
Xcode unsigned build
        ↓
TrollStore IPA
```

上流更新時は、この順番で壊れた場所を確認する。

---

## 4. 上流ファイルへの依存度

### 4.1 高リスク — `Program.cs`

主に `patches/apply_ios_mods.py` と `patches/finalize_offline_ai.py` が依存。

現在の変更:

- 起動時の強制 `RetryBasicDataUpdate()` を抑止
- bundled basic dataを使用
- BasicData状態表示の修正
- iOSでtexture download同時数を抑制
- iOSで1frameあたりtexture生成数を抑制
- タッチフレームでtexture pumpを避ける
- resize時の `Resources.UnloadUnusedAssets` をiOSで回避
- `IPhone16x9Viewport.EnsureInstalled()`
- `MainScriptBootstrap.EnsureInstalled()`
- 旧AI bootstrapの除去

**上流Program.csが大きく変わった場合、文字列anchorを無理に合わせない。**
新しい上流処理を読み、同じ目的がまだ必要かを再判断する。

### 4.2 高リスク — `Menu.cs`

`apply_ios_mods.py` / `finalize_offline_ai.py` が依存。

現在の目的:

- AIメニュー項目 `ai_` のイベント復元
- AI項目の可視化
- メニュー列内での位置調整
- 既存Super-Pre / Resource Updateを壊さない

上流がAI項目を正式に復活させた場合は、既存patchを削減できる可能性が高い。

### 4.3 高リスク — `AIRoom.cs`

最終的には:

- `patches/ai/windbot/AIRoom.cs`

で置換する。

現在の独自機能:

- ローカルWindBot起動
- 複数AIデッキ選択
- 左右2本の独立 `UIselectableList` による自分/AIデッキ同時表示
- 中央の操作不可選択表示（旧 `rank_` / `aideck_` を転用）
- player deck選択
- 「シャッフルしない」設定
- 固定8000LP / Master Rule 2020
- Koishi既存RPS UIを再利用したローカルじゃんけんと先攻後攻決定

上流AIRoomの新機能は自動では取り込まれない。
KoishiPro2更新時は**新しい上流AIRoomと独自AIRoomを必ず比較**し、有用な新機能があれば手動統合する。

現在は通常対戦側のじゃんけんUIを再利用するため、固定KoishiPro2の
`Room.cs` / `Servant.cs` にある `RMSshow_tp`, `RMSshow_FS`,
`new_ui_handShower`, `Program.go` にも依存する。
また、AI/プレイヤーデッキ一覧は `transUI/UIselectableList.cs` と
`UIselectableListItem.cs` の45px行スクロール実装を共用する。
AI側一覧は `trans_AIroom.prefab` の既存 `deck` オブジェクトをruntime複製して作る。
現固定ソースでは `panel_` と `bar_` が `deck` の子なので、複製後も左右で独立する。
上流更新時はこの階層構造とUI API/テンプレートを比較する。

### 4.4 中リスク — `KoishiWindBotBridge.cs`

独自追加ファイル。

依存先:

- KoishiPro2の `Program`
- `YGOSharp.Card`
- `YGOSharp.CardsManager`
- `Ocgcore`
- UI servant構造

過去に固定Koishiソースとの型差で:

- `Setcode: long → ulong`
- `RuleCode` 不在

が発生している。

上流更新時はYGOSharpのCard型/API差分を確認する。

### 4.5 中リスク — WindBot managed runtime

主なファイル:

- `windbot/prepare_windbot_subset.sh`
- `windbot/runtime/LocalDuelNative.cs`
- `windbot/runtime/LocalDuelRouter.cs`
- `windbot/runtime/RadiantTyphoonLocalDuel.cs`

WindBot upstreamからAI Executor / YDKを収集し、iOS/IL2CPP向けregistryを生成する。

**Reflection / Activator.CreateInstanceへ戻さない。**
IL2CPP向けに直接 `new XxxExecutor(...)` する生成方式を維持する。

WindBot側のconstructor/API変更時はsubset generatorを修正する。

### 4.6 中リスク — ocgcore

`windbot/build_koishi_core_ios.sh` で現在のKoishi系coreをビルド。

追加patch:

- `windbot/patch_koishi_core_alias_script.py`

目的:

- 近接aliasはalias側Lua
- 遠いaliasは本人code側Lua
- 原作/アニメ版等の独自Luaを維持

**上流coreが同等以上のalias処理へ更新された場合、このpatchは削除候補。**
anchorが見つからなくなったからといって、別箇所へ機械的に差し替えない。

### 4.7 中〜高リスク — Unity/Xcode viewport

- managed側: `patches/ios/IPhone16x9Viewport.cs`
- export後: `patches/prepare_notch_compat.py`

現在iPhone X実機で:

- 縦横比正常
- Landscape左右ノッチ位置正常
- ノッチと描画領域の余分な隙間解消

を確認済み。

Unity更新やXcode出力構造変更時は必ず実機再確認する。

---

## 5. 削除・縮小できる可能性があるpatch

上流更新時は「全部再適用」を前提にしない。

| patch / 機能 | 削除候補になる条件 |
| --- | --- |
| 起動時DB同期抑止 | 上流が起動時に不要なネット同期を行わなくなる |
| BasicData 0%表示修正 | 上流で状態表示が正常化 |
| iOS texture負荷制限 | 上流でiPhone Xの数秒フリーズが解消 |
| resize時UnloadUnusedAssets回避 | 上流で同等対策済み |
| AI menu復元 | 上流がAIメニューを正式提供 |
| legacy AI関連patch | WindBot-only構成で完全不要と判断できた場合 |
| alias script patch | coreが現行EDOPro相当のalias判定を実装 |
| native notch patch | 上流/Unityだけで現在と同じ実機レイアウトになる |

一方、以下はこのプロジェクト固有機能なので基本的に維持する:

- ローカルWindBot統合
- 複数WindBot AI
- `expansions/*.cdb` のhost card fallback
- `expansions/script/c<ID>.lua` fallback
- TrollStore向けunsigned IPA build

---

## 6. KoishiPro2本体更新手順

### Step 0 — 現在の正常基準を固定

更新前に必ず:

1. `PROJECT_STATE.md` を読む
2. 最新の実機正常commitを確認
3. 最新成功CI runを確認
4. 現在のIPAを残す
5. 現在のrevisionファイルを残す

更新作業中に正常基準commitを上書きしない。

### Step 1 — 更新候補commitを調査

まず新しいKoishiPro2 commitを読む。

特に比較する:

- `Program.cs`
- `Menu.cs`
- `AIRoom.cs`
- `YGOSharp/Card.cs`
- `YGOSharp/CardsManager.cs`
- AI関連ファイル
- iOS / Unity関連変更

この時点ではproductionの `SOURCE_COMMIT` を変更しない。

### Step 2 — 既存patchがまだ必要か分類

各変更を:

- **KEEP** — まだ必要
- **ADAPT** — 目的は必要だがanchor/APIが変わった
- **DROP** — 上流に取り込まれ不要
- **INVESTIGATE** — 挙動不明

に分類する。

### Step 3 — candidate branch/commitでSOURCE_COMMITを変更

更新する場所:

- `.github/workflows/ios-cloud-build.yml`
- `.github/workflows/ai-integration-validate.yml`

両方同じcommitにする。

必要ならsource cache schema/keyも更新するが、
単に古いcacheを使わせない目的だけで無関係な定数を変えない。

### Step 4 — patch適用だけを先に検証

いきなりUnityフルビルドへ進まない。

優先順:

1. Python構文 / shell構文
2. `apply_ios_mods.py`
3. `sync_main_scripts.sh`
4. `finalize_offline_ai.py`
5. `activate_windbot_ai.py`
6. `prepare_windbot_runtime.sh`
7. core build

**anchor not found は正常な安全停止。**
その場で新上流を読み、patchを再設計する。
広すぎるregexや「見つからなければ無視」は使わない。

### Step 5 — integration validation

必須:

- **KoishiPro2 WindBot integration validation**

確認:

- AI menuが1回だけ登録
- legacy Percy参照なし
- WindBot bridge/runtime存在
- 複数AI registry生成
- current ocgcore symbols存在
- main scripts同梱
- expansion fallback経路維持

### Step 6 — WindBot/core独立テスト

必須:

- **WindBot multi-deck C# validation**
- **WindBot local duel smoke test**
- **WindBot iOS runtime validation**

最低確認:

- 複数Executor compile
- Radiant Typhoon AIターン完走
- Radiant以外のExecutor生成
- expansion-only CDB card data fallback
- `expansions/script` fallback
- 遠いaliasカードが本人Luaを使用
- iOS arm64 core link probe

### Step 7 — iOS cloud build

上記が通ってから:

- **KoishiPro2 iOS cloud build**

Unity認証が必要な場合は認証。

確認:

- Unity C# compile
- IL2CPPにWindBot runtime到達
- Xcode export
- notch patch適用
- unsigned app build
- IPA artifact生成

### Step 8 — 実機回帰テスト

最低限:

1. 起動
2. メニュー操作速度
3. AI画面を開く
4. 左右のデッキ一覧が同時表示され、AIデッキ全件を右一覧でスクロール可能
5. Radiant Typhoon対戦
6. Radiant以外のAI対戦
7. 「シャッフルしない」ON/OFF
8. 「自分が先攻」OFF時のじゃんけん・先後決定
9. 通常カード効果
10. `expansions/*.cdb` の追加カード
11. `expansions/script/c<ID>.lua`
12. 遠いaliasを持つ原作版カードが本人Luaを使用
13. iPhone X横画面の左右回転
14. ノッチ余白
15. デッキ編集/通常メニュー等、AI以外の既存機能

### Step 9 — 正常基準を更新

実機確認後にのみ:

- `PROJECT_STATE.md`
- この `UPSTREAM_UPDATE.md`

へ:

- 新 `SOURCE_COMMIT`
- 成功build番号
- 成功CI
- 削除したpatch
- 新たに必要になったpatch
- 実機確認結果

を記録する。

---

## 7. WindBot / ocgcore / scriptだけ更新された場合

KoishiPro2本体SOURCE_COMMITが同じでもruntime更新で不具合が出る可能性がある。

その場合:

1. `koishi-runtime-revisions.txt` を前回正常版と比較
2. `windbot-revision.txt` を比較
3. `koishi-main-scripts-revision.txt` を比較
4. 変わったcomponentだけ疑う
5. KoishiPro2 `SOURCE_COMMIT` は動かさない

例:

- AIだけ壊れた → WindBot revision / Executor API
- 効果処理だけ壊れた → core / script revision
- UIだけ壊れた → KoishiPro2 managed patch
- iPhone表示だけ壊れた → Unity/Xcode/notch patch

---

## 8. patch作成時の更新耐性ルール

今後の新規改変は以下を守る。

1. **上流ファイルを丸ごとコピーしない**
   - 独自実装ファイルを除く
2. 既存上流ファイル変更は小さなanchor patchを優先
3. anchorは意味のある固有コードを使う
4. expected countを検証する
5. anchorが変わったらfail-fast
6. 「見つからないので無視」は避ける
7. 独自ロジックは `patches/` または `windbot/` に隔離
8. upstream sourceへ手作業で直接修正しない
9. core変更も専用patch script化
10. 修正理由を `PROJECT_STATE.md` に残す
11. 上流に同等修正が入ったら独自patchを削除する
12. CIで検証できる不具合は必ず回帰テスト化する

---

## 9. 衝突時にしてはいけないこと

- 新上流へ旧 `Program.cs` を丸ごと上書き
- 新上流へ旧 `Menu.cs` を丸ごと上書き
- anchorを確認せずregexを広げる
- validationを通すためだけに検査を削る
- WindBot側で `expansions/*.cdb` を再読込する
- ReflectionでAI Executor生成へ戻す
- alias問題を直すためCDB aliasを一律0へ変更
- iPhone画面問題で旧16:9固定へ戻す
- KoishiPro2本体更新とWindBot/core更新を同時に行い、原因追跡不能にする

---

## 10. ロールバック

上流更新に失敗した場合は、まずproductionの:

```
SOURCE_COMMIT=c1850e1120df6a5885692eadacad8ad114a3cf11
```

へ戻す。

ただしruntime upstreamが既に進んでいる可能性があるため、
完全再現が必要なら正常版の:

- `koishi-runtime-revisions.txt`
- `windbot-revision.txt`
- `koishi-main-scripts-revision.txt`

も確認する。

将来的に完全再現性が必要になった場合は、
WindBot / ygopro / core / scriptもcommit pin方式へ移行することを検討する。

---

## 11. 更新作業の完了条件

以下をすべて満たすまで「更新成功」としない。

- patch適用がfail-fast検証込みで成功
- multi-deck C# validation成功
- local duel smoke成功
- iOS runtime validation成功
- integration validation成功
- iOS cloud build成功
- 実機でAI対戦成功
- 実機で追加カード成功
- 実機でalias独自Lua成功
- 実機でiPhone X表示正常
- `PROJECT_STATE.md` 更新済み

