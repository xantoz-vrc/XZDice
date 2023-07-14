Changelog
=========

## v1.5 VCC

### 日本語
* VCCとU＃１.xに対応しました
* 日本語に対応しました（マルチプレイヤー版のみ）
  - ギッミクに入ってるボタンで英語と切り替える。デフォルト言語はUdonBehaviourのlangJpパラメーターで切り替えができます

### 英語
* Now compatible with VCC and U#1.x
* Japanese language support (Currently only the Multiplayer variation)
  - You can switch language with the button included in the gimmick Prefab. The default language can be toggled with the langJp parameter in the UdonBehaviour


日本語
======

## UdonChips チンチロ ワールドギッミク

導入方法
1. VRChat Creator CompanionでVRCSDK WorldとUdonSharpを入れてください
2. Window→TextMeshProメニューからImport TMP Essential ResourcesとImport TMP Examples & ExtrasでTextMeshProとフォントをインポートしてください
3．UdonChips StarterSet VCC版( https://booth.pm/ja/items/3060394 )をインポートして、UdonChipsのPrefabを一個だけSceneに入れてください
4．"xzdice 01.unitypackage"をインポート
5．"Assets/XZDice/Prefabs"にあるChinchirorin.prefabかChinchirorinSinglePlayer.prefabをSceneに置いてください
6. デバッグログが必要ない場合は4．でSceneに置いたPrefabを「Unpack Prefab」して、「GameLogDebug」のGameObjectを消すと消えます。「Unpack Prefab」したくない場合は「GameLogDebug」からチェック外してもOKです。
7. (任意）UdonChipsScoreboard.prefabをSceneに置いたら、プレイヤーがお互いのUdonChips残高を確かめ合う事が出来ます。

Chinchirorin.prefabはマルチプレイヤー版で、2人から4人まで遊べます。
ChinchirorinSinglePlayer.prefabは一人で遊べる未同期シングルプレイヤー版です。

Githubにもあります：
https://github.com/xantoz-vrc/xzdice
IssueやPR歓迎です（日本語でも対応します）

お椀のモデルは「さくらんぼクリエーション」さまのを使っています（同梱の許可は得ています）https://booth.pm/ja/items/2074816

規約：
規約はOpen Source SoftwareのMITライセンスになっています。UnityPackage内のLICENSEかこのページの一番下をご参照ください。

ENGLISH
=======

## UdonChips Chinchirorin world Gimmick

How to import:
1. Import VRCSDK Worlds and UdonSharp using VRChat Creator Companion
2. Import TextMeshPro and its extra fonts using "Import TMP Essential Resources" and "Import TMP Examples & Extras" from the Window -> TextMeshPro dropdown menu
3. Import UdonChips StarterSet VCC ( https://booth.pm/en/items/3060394 ), then put exactly one instance of the UdonChips prefab in your scene
4．Import "xzdice 01.unitypackage"
5．Place one or both of Assets/XZDice/Prefabs/Chinchirorin.prefab and Assets/XZDice/Prefabs/ChinchirorinSinglePlayer.prefab in your scene.
6. If you do not need debug logging, unpack the prefab and remove the "GameLogDebug" object. If you do not wish to unpack the prefab you can instead just deactive the "GameLogDebug" object.
7. (Optional) You can place UdonChipsScoreboard.prefab in your scene to give the players a way to see each others' UdonChips balance

`Chinchirorin.prefab` is the 2-4 player multiplayer version. `ChinchirorinSinglePlayer.prefab` is the non-networked single player version.

You can find the source code, make suggestions, or even make PRs on GitHub: https://github.com/xantoz-vrc/XZDice

The bowl model used is from "さくらんぼクリエーション" https://booth.pm/ja/items/2074816

## License
Copyright 2023 Xantoz

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
