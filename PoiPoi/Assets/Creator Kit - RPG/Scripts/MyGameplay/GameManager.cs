﻿using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Photon.Pun;
using UnityEngine;

namespace RPGM.Gameplay
{
    /// <summary>
    /// ゲームマネージャー
    /// </summary>
    public class GameManager : MonoBehaviourPunCallbacks
    {
        GameModel model = Core.Schedule.GetModel<GameModel>();

        /// <summary>
        /// ゲームフェーズ
        /// </summary>
        public enum GamePhaseType
        {
            /// <summary>
            ///  オープニング
            /// </summary>
            Opening,

            /// <summary>
            /// ゲーム中
            /// </summary>
            InGame,

            /// <summary>
            /// エンディング
            /// </summary>
            Ending,
        }

        [SerializeField]
        float timer;

        public GamePhaseType GamePhase { get; set; }
        public bool Practice { get => roomProperty_practice; set => roomProperty_practice = value; }
        public int PlayerGameNo { get => PhotonNetwork.LocalPlayer?.GetGameNo() ?? -1; }

        /// <summary>
        /// Photonルームプロパティ：ゲーム開始フラグ
        /// </summary>
        private bool roomProperty_start
        {
            get => (PhotonNetwork.CurrentRoom?.CustomProperties["Start"] is bool value) ? value : false;
            set
            {
                if (PhotonNetwork.IsMasterClient)
                {
                    var hashtable = new ExitGames.Client.Photon.Hashtable();
                    hashtable["Start"] = value;
                    PhotonNetwork.CurrentRoom?.SetCustomProperties(hashtable);
                }
            }
        }

        /// <summary>
        /// Photonルームプロパティ：プラクティスモードフラグ
        /// </summary>
        private bool roomProperty_practice
        {
            get => (PhotonNetwork.CurrentRoom?.CustomProperties["Practice"] is bool value) ? value : false;
            set
            {
                if (PhotonNetwork.IsMasterClient)
                {
                    var hashtable = new ExitGames.Client.Photon.Hashtable();
                    hashtable["Practice"] = value;
                    PhotonNetwork.CurrentRoom?.SetCustomProperties(hashtable);
                }
            }
        }

        private CancellationToken cancelToken => this.GetCancellationTokenOnDestroy();

        // Start is called before the first frame update
        void Start()
        {
            GamePhase = GamePhaseType.Opening;
            PhotonNetwork.LocalPlayer.SetScoreAndRecord(0, -1.0f);
            UI.Timer.SetTime(timer);

            // オープニングシーン開始
            OpeningCoroutine().Forget();
        }

        // Update is called once per frame
        void Update()
        {
            if (GamePhase == GamePhaseType.InGame)
            {
                if (Practice)
                {
                    // 練習モード中は時間を止める
                    UI.Timer.SetTime(-1.0f);
                }
                else
                {
                    // 時間経過処理
                    timer -= Time.deltaTime;
                    if (timer <= 0.0f)
                    {
                        // ゲーム終了
                        timer = 0.0f;
                        // エンディングシーン開始
                        EndingCoroutine().Forget();
                    }
                    UI.Timer.SetTime(timer);
                }
            }

            // 全プレイヤーのスコア表示を更新
            foreach (var player in PhotonNetwork.PlayerList)
            {
                if (player.GetReady())
                {
                    model.scores[player.GetGameNo()].SetScore(player.GetScore());
                }
            }
        }

        /// <summary>
        /// スコアの追加と記録更新判定
        /// </summary>
        /// <param name="addScore">追加スコア</param>
        /// <param name="distance">飛距離</param>
        /// <returns></returns>
        public bool UpdateScoreAndRecord(int addScore, float distance)
        {
            // 練習モード中は記録更新しない
            if (Practice) { return false; }

            int score = PhotonNetwork.LocalPlayer.GetScore() + addScore;
            if (PhotonNetwork.LocalPlayer.GetRecord() < distance)
            {
                // スコアの追加と記録更新
                PhotonNetwork.LocalPlayer.SetScoreAndRecord(score, distance);
                return true;
            }
            else
            {
                // スコアの追加のみ
                PhotonNetwork.LocalPlayer.SetScore(score);
                return false;
            }
        }

        /// <summary>
        /// ルームのカスタムプロパティが変更されたときに呼び出されます。propertiesThatChangedには、Room.SetCustomPropertiesで設定されたものがすべて含まれています。
        /// </summary>
        /// <remarks>
        /// v1.25以降、このメソッドは1つのパラメータ、Hashtable propertiesThatChangedを持ちます。
        /// プロパティの変更はRoom.SetCustomPropertiesによって行われる必要があります。これにより、このコールバックもローカルで発生します。
        /// </remarks>
        /// <param name="propertiesThatChanged">設定されたカスタムプロパティ</param>
        public override void OnRoomPropertiesUpdate(ExitGames.Client.Photon.Hashtable propertiesThatChanged)
        {
#if false
            // 更新されたルームのカスタムプロパティのペアをコンソールに出力する
            foreach (var prop in propertiesThatChanged)
            {
                Debug.Log($"{prop.Key}: {prop.Value}");
            }
#endif
        }

        /// <summary>
        /// 全プレイヤーの中で最大の得点を取得
        /// </summary>
        /// <returns>最大得点</returns>
        private int GetMaxScore()
        {
            int maxScore = 0;
            foreach (var player in PhotonNetwork.PlayerList)
            {
                if (maxScore < player.GetScore())
                {
                    maxScore = player.GetScore();
                }
            }
            return maxScore;
        }

        /// <summary>
        /// 全プレイヤーの中で最長の記録を取得
        /// </summary>
        /// <returns>最大得点</returns>
        private float GetMaxRecord()
        {
            float maxRecord = 0;
            foreach (var player in PhotonNetwork.PlayerList)
            {
                if (maxRecord < player.GetRecord())
                {
                    maxRecord = player.GetRecord();
                }
            }
            return maxRecord;
        }

        /// <summary>
        /// オープニングシーンコルーチン
        /// </summary>
        /// <returns>UniTaskVoid</returns>
        private async UniTaskVoid OpeningCoroutine()
        {
            GamePhase = GamePhaseType.Opening;
            UI.FadeScreen.FadeIn(1.0f, Color.white);
            await UniTask.Delay(1500, cancellationToken: cancelToken);

            // Photonがルームに入室するまで待つ
            await UniTask.WaitUntil(() => PhotonNetwork.InRoom, cancellationToken: cancelToken);

            // プレイヤーのゲーム番号が決まるまでループ（他のプレイヤーと重複させないための処理）
            for (int i = 0; ; i = (i + 1) % NetworkManager.RoomMaxPlayers)
            {
                // いったん設定して、その設定が返ってくるのを待つ
                PhotonNetwork.LocalPlayer.SetGameNo(i);
                await UniTask.WaitUntil(() => PhotonNetwork.LocalPlayer.GetGameNo() == i, cancellationToken: cancelToken);

                // 他に同じ番号を設定しているプレイヤーが居なければOK
                if (!System.Array.Exists(PhotonNetwork.PlayerListOthers, p => p.GetGameNo() == i))
                {
                    // 設定OK
                    PhotonNetwork.LocalPlayer.SetReady(true);
                    break;
                }
            }

            // ゲーム開始前の待機画面　オンラインの場合他のユーザーを待ったりする
            bool lobbyResult = await LobbyCoroutine(cancelToken);
            if (!lobbyResult)
            {
                // トップメニューに戻る
                await ReturnTopMenu(cancelToken);
                return;
            }

            // ランダムな座標に自身のアバター（ネットワークオブジェクト）を生成する
            Random.InitState((int)(Time.time * 100));
            var position = new Vector3(Random.Range(2f, 4f), Random.Range(9f, 11f));
            var player_obj = PhotonNetwork.Instantiate("Character", position, Quaternion.identity);
            model.player = player_obj.GetComponent<CharacterController2D>();

            UI.MessageBoard.Show("Let's ....");
            await UniTask.Delay(1500, cancellationToken: cancelToken);

            UI.MessageBoard.Show("Poi Poi !!!!");
            await UniTask.Delay(1500, cancellationToken: cancelToken);

            model.cameraController.SetDefaultFocus(model.player.transform);
            model.cameraController.ChangeInGameCamera();
            UI.MessageBoard.Hide();
            for (int i = 0; i < model.scores.Length; i++)
            {
                if (System.Array.Exists(PhotonNetwork.PlayerList, p => p.GetGameNo() == i))
                {
                    model.scores[i].Show(i, !PhotonNetwork.OfflineMode);
                }
                else
                {
                    model.scores[i].Hide();
                }
            }
            UI.Timer.Show();
            GamePhase = GamePhaseType.InGame;
        }

        /// <summary>
        /// ロビーシーンコルーチン
        /// </summary>
        /// <param name="cancellationToken">キャンセルトークン</param>
        /// <returns>UniTask(bool=ゲーム開始なら真、ゲームから抜けたら偽)</returns>
        private async UniTask<bool> LobbyCoroutine(CancellationToken cancellationToken)
        {
            float horizontal = 0.0f;
            int controlStyleNum = System.Enum.GetValues(typeof(UI.InputController.ControlStyle)).Length;

            if (!PhotonNetwork.OfflineMode)
            {
                UI.Lobby.Show();
            }

            // ゲーム開始待ち
            while (!roomProperty_start)
            {
                // 準備完了したユーザーの表示
                if (!PhotonNetwork.OfflineMode)
                {
                    for (int i = 0; i < NetworkManager.RoomMaxPlayers; i++)
                    {
                        var player = System.Array.Find(PhotonNetwork.PlayerList, p => p.GetGameNo() == i);
                        if (player != null && player.GetReady())
                        {
                            UI.Lobby.PlayerOn(i, player.IsLocal);
                        }
                        else
                        {
                            UI.Lobby.PlayerOff(i);
                        }
                    }
                }

                // 操作説明表示
                switch (model.input.controlStyle)
                {
                    default:
                    case UI.InputController.ControlStyle.ChargeAim_Reverse:
                        UI.MessageBoard.Show(
                            "Move : L Stick\n" +
                            "Pick: R Trigger\n" +
                            "Charge and Aim: R Stick\n" +
                            "(Aim Reverse)",
                            PhotonNetwork.IsMasterClient,
                            controlStyleNum >= 2,
                            controlStyleNum >= 2);
                        break;
                    case UI.InputController.ControlStyle.ChargeAim:
                        UI.MessageBoard.Show(
                            "Move : L Stick\n" +
                            "Pick: R Trigger\n" +
                            "Charge and Aim: R Stick",
                            PhotonNetwork.IsMasterClient,
                            controlStyleNum >= 2,
                            controlStyleNum >= 2);
                        break;
                    case UI.InputController.ControlStyle.NewControl_Reverse:
                        UI.MessageBoard.Show(
                            "Move : L Stick\n" +
                            "Aim : R Stick\n" +
                            "Pick or Charge: R Trigger\n" +
                            "(Aim Reverse)",
                            PhotonNetwork.IsMasterClient,
                            controlStyleNum >= 2,
                            controlStyleNum >= 2);
                        break;
                    case UI.InputController.ControlStyle.NewControl:
                        UI.MessageBoard.Show(
                            "Move : L Stick\n" +
                            "Aim : R Stick\n" +
                            "Pick or Charge: R Trigger",
                            PhotonNetwork.IsMasterClient,
                            controlStyleNum >= 2,
                            controlStyleNum >= 2);
                        break;
                    case UI.InputController.ControlStyle.Classic:
                        UI.MessageBoard.Show(
                            "Move or Aim : L Stick\n" +
                            "Pick or Charge : A\n" +
                            "Dash : R Trigger\n" +
                            "Stay : L Trigger",
                            PhotonNetwork.IsMasterClient,
                            controlStyleNum >= 2,
                            controlStyleNum >= 2);
                        break;
                }

                // 操作切り替え
                if (controlStyleNum >= 2)
                {
                    float newHorizontal = Input.GetAxis("Horizontal");
                    if (newHorizontal < -0.5f && horizontal >= -0.5f)
                    {
                        model.input.controlStyle = (UI.InputController.ControlStyle)Mathf.Repeat((float)model.input.controlStyle - 1, controlStyleNum);
                        UI.UserInterfaceAudio.OnPick();
                    }
                    if (newHorizontal > 0.5f && horizontal <= 0.5f)
                    {
                        model.input.controlStyle = (UI.InputController.ControlStyle)Mathf.Repeat((float)model.input.controlStyle + 1, controlStyleNum);
                        UI.UserInterfaceAudio.OnPick();
                    }
                    horizontal = newHorizontal;
                }

                // マスターの人がボタン押下したらゲーム開始
                if (PhotonNetwork.IsMasterClient)
                {
                    if (Input.GetButtonDown("Submit"))
                    {
                        PhotonNetwork.CurrentRoom.IsOpen = false;
                        roomProperty_start = true;
                        break;
                    }
                }

                // キャンセルボタン押下した人はゲームから抜ける
                if (Input.GetButtonDown("Cancel"))
                {
                    // ゲームから抜ける
                    return false;
                }

                await UniTask.Yield(cancellationToken);
            }

            // ロビーを閉じてしゲーム開始
            if (!PhotonNetwork.OfflineMode)
            {
                UI.Lobby.Hide();
            }
            return true;
        }

        /// <summary>
        /// エンディングシーンコルーチン
        /// </summary>
        /// <returns>UniTaskVoid</returns>
        private async UniTaskVoid EndingCoroutine()
        {
            GamePhase = GamePhaseType.Ending;
            for (int i = 0; i < model.scores.Length; i++)
            {
                model.scores[i].Hide();
            }
            UI.Timer.Hide();
            UI.MessageBoard.Show("Finish !!!!");
            model.cameraController.SetOutGameCameraFocus(model.player.transform);
            model.cameraController.ChangeOutGameCamera();
            await UniTask.Delay(3000, cancellationToken: cancelToken);

            // リザルト表示
            UI.MessageBoard.Hide();
            foreach (var player in PhotonNetwork.PlayerList)
            {
                bool you = false;
                bool winner = false;
                bool longest = false;
                if (!PhotonNetwork.OfflineMode)
                {
                    you = player.IsLocal;
                    winner = player.GetScore() >= GetMaxScore();
                    longest = player.GetRecord() >= GetMaxRecord();
                }
                UI.Result.ShowPlayer(player.GetGameNo(), you, player.GetScore(), player.GetRecord(), winner, longest);
            }
            UI.Result.Show();
            await UniTask.Delay(500, cancellationToken: cancelToken);

            await UniTask.WaitUntil(() => Input.GetButtonDown("Submit"), cancellationToken: cancelToken);

            UI.Result.Hide();
            await UniTask.Delay(1000, cancellationToken: cancelToken);

            UI.FadeScreen.FadeOut(1.0f, Color.black);
            await UniTask.Delay(1500, cancellationToken: cancelToken);

            // トップメニューに戻る
            await ReturnTopMenu(cancelToken);
        }

        /// <summary>
        /// トップメニューに戻るコルーチン
        /// </summary>
        /// <param name="cancellationToken">キャンセルトークン</param>
        /// <returns>UniTask</returns>
        private async UniTask ReturnTopMenu(CancellationToken cancellationToken)
        {
            if (PhotonNetwork.OfflineMode)
            {
                // オフラインモードから切断
                PhotonNetwork.OfflineMode = false;
                await UniTask.WaitUntil(() => !PhotonNetwork.IsConnected, cancellationToken: cancellationToken);
            }
            else
            {
                // サーバーから切断
                PhotonNetwork.Disconnect();
                await UniTask.WaitUntil(() => !PhotonNetwork.IsConnected, cancellationToken: cancellationToken);
            }

            // トップシーンのロード
            UnityEngine.SceneManagement.SceneManager.LoadScene("TopScene");
        }
    }
}
