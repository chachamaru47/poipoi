﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Photon.Pun;
using RPGM.Gameplay;
using UnityEngine;
using UnityEngine.U2D;

namespace RPGM.Gameplay
{
    /// <summary>
    /// A simple controller for animating a 4 directional sprite using Physics.
    /// </summary>
    public class CharacterController2D : MonoBehaviourPunCallbacks, IPunObservable
    {
        public float speed = 1;
        public float acceleration = 2;
        public Animator animator;
        //public bool flipX = false;

        public Collider2D searchCollider;
        public GameObject socket;
        public UI.PowerGauge powerGauge;
        public GameObject namePlate;
        public TMPro.TextMeshProUGUI nameText;

        public Vector3 nextMoveCommand { get; set; }
        public Vector3 nextAimCommand { get; set; }
        public bool onPick { get; set; } = false;
        public bool onCharge { get; set; } = false;
        public bool offCharge { get; set; } = false;
        public bool onFire { get; set; } = false;
        public bool isAiming { get; set; } = false;
        public bool isChargeLoop { get; set; } = false;
        public float chargeSpeed { get; set; } = 1.0f;

        new Rigidbody2D rigidbody2D;
        SpriteRenderer spriteRenderer;
        PixelPerfectCamera pixelPerfectCamera;
        GameModel model = Core.Schedule.GetModel<GameModel>();

        enum State
        {
            Idle, Moving
        }

        State state = State.Idle;
        Vector3 start, end;
        Vector2 currentVelocity;
        float startTime;
        float distance;
        float velocity;
        bool bCharge = false;
        float chargeTime;

        DropItem useItem = null;

        /// <summary>
        /// プレイヤーのゲーム番号ごとの文字色を取得する
        /// </summary>
        /// <param name="playerGameNo">プレイヤーゲーム番号</param>
        /// <returns>プレイヤー色</returns>
        public static Color GetPlayerTextColor(int playerGameNo)
        {
            Color color;
            switch (playerGameNo % NetworkManager.RoomMaxPlayers)
            {
                default:
                case 0:
                    color = Color.white;
                    break;
                case 1:
                    color = Color.magenta;
                    break;
                case 2:
                    color = Color.yellow;
                    break;
                case 3:
                    color = Color.cyan;
                    break;
                case 4:
                    color = Color.green;
                    break;
            }
            return color;
        }

        /// <summary>
        /// プレイヤーのゲーム番号ごとのキャラ色を取得する
        /// </summary>
        /// <param name="playerGameNo">プレイヤーゲーム番号</param>
        /// <returns>プレイヤー色</returns>
        public static Color GetPlayerCharacterColor(int playerGameNo)
        {
            Color color;
            switch (playerGameNo % NetworkManager.RoomMaxPlayers)
            {
                default:
                case 0:
                    color = Color.white;
                    break;
                case 1:
                    color = new Color(1.0f, 0.5f, 1.0f);
                    break;
                case 2:
                    color = new Color(1.0f, 1.0f, 0.0f);
                    break;
                case 3:
                    color = new Color(0.3f, 0.8f, 1.0f);
                    break;
                case 4:
                    color = new Color(0.5f, 1.0f, 0.5f);
                    break;
            }
            return color;
        }

        void IdleState()
        {
            if (nextMoveCommand != Vector3.zero)
            {
                start = transform.position;
                end = start + nextMoveCommand;
                distance = (end - start).magnitude;
                velocity = 0;
                UpdateAnimator(nextMoveCommand, true);
                nextMoveCommand = Vector3.zero;
                state = State.Moving;
            }
        }

        void MoveState()
        {
            Vector3 dirCommand = (isAiming) ? nextAimCommand : nextMoveCommand;
            velocity = Mathf.Clamp01(velocity + Time.deltaTime * acceleration);
            bool stay = nextMoveCommand.magnitude < 0.2f;
            UpdateAnimator(dirCommand, stay);
            float apply_speed = speed;
            if (useItem != null)
            {
                // アイテムの重さを見て減速
                apply_speed *= Mathf.Lerp(1.0f, 0.1f, useItem.data.mass / 5.0f);
            }
            rigidbody2D.velocity = Vector2.SmoothDamp(rigidbody2D.velocity, nextMoveCommand * apply_speed, ref currentVelocity, acceleration, speed);
            spriteRenderer.flipX = (isAiming ? dirCommand.x : rigidbody2D.velocity.x) >= 0 ? true : false;
        }

        void UpdateAnimator(Vector3 direction, bool stay)
        {
            if (animator)
            {
                bool yanim = Mathf.Abs(direction.x) < Mathf.Abs(direction.y);
                int wx = (yanim) ? 0 : (direction.x < 0) ? -1 : (direction.x > 0) ? 1 : 0;
                int wy = (!yanim) ? 0 : (direction.y < 0) ? 1 : (direction.y > 0) ? -1 : 0;
                animator.SetInteger("WalkX", wx);
                animator.SetInteger("WalkY", wy);
                animator.SetBool("Stay", stay);
            }
        }

        void Update()
        {
            if (!photonView.IsMine)
            {
                return;
            }

            switch (state)
            {
                case State.Idle:
                    IdleState();
                    break;
                case State.Moving:
                    MoveState();
                    break;
            }

            powerGauge.Hide();
            model.cameraController.positionOffset = Vector3.zero;

            if (useItem == null)
            {
                // アイテムを持ってない時

                bCharge = false;
                chargeTime = 0.0f;

                // アイテムサーチ
                if (onPick)
                {
                    SearchCoroutine(this.GetCancellationTokenOnDestroy()).Forget();
                }
            }
            else
            {
                // パワーゲージの角度を移動入力の角度に合わせる
                float rad = Mathf.Atan2(nextAimCommand.y, nextAimCommand.x);
                powerGauge.transform.eulerAngles = new Vector3(0.0f, 0.0f, rad * Mathf.Rad2Deg);

                // エイム入力があったら
                if (isAiming)
                {
                    // パワーゲージを表示
                    powerGauge.Show(0.0f);

                    // カメラのフォーカス対象がデフォルト状態なら入力方向にカメラを振る
                    if (model.cameraController.IsDefaultFocus())
                    {
                        model.cameraController.positionOffset = nextAimCommand.normalized * 5.0f;
                    }
                }

                // チャージ中
                float chargePower = 0.0f;
                if (bCharge)
                {
                    chargeTime += chargeSpeed * Time.deltaTime;
                    if (isChargeLoop)
                    {
                        chargeTime = Mathf.Repeat(chargeTime, 1.1f);
                    }
                    if (chargeTime < 1.1f)
                    {
                        if (nextAimCommand.magnitude != 0.0f)
                        {
                            chargePower = chargeTime;
                            powerGauge.Show((chargeTime > 1.0f) ? 1.0f : chargeTime / 1.0f);
                        }
                    }
                    else
                    {
                        if (nextAimCommand.magnitude != 0.0f)
                        {
                            powerGauge.Show(0.0f);
                        }
                    }
                }

                if (onCharge)
                {
                    // チャージ開始
                    bCharge = true;
                }

                // 攻撃ボタン離す
                if (onFire)
                {
                    // チャージ中ならアイテム投射
                    if (bCharge)
                    {
                        // アイテム投射
                        Vector2 force = nextAimCommand.normalized * chargePower * 20.0f;
                        ThrowItem(force);
                    }
                }

                if (offCharge)
                {
                    bCharge = false;
                    chargeTime = 0.0f;
                }
            }
        }

        void LateUpdate()
        {
            if (!photonView.IsMine)
            {
                return;
            }

            if (pixelPerfectCamera != null)
            {
                transform.position = pixelPerfectCamera.RoundToPixel(transform.position);
            }
        }

        void Awake()
        {
            int ownerGameNo = photonView.Owner.GetGameNo();

            rigidbody2D = GetComponent<Rigidbody2D>();
            if (!photonView.IsMine)
            {
                // 自分のキャラ以外は挙動を通信で受け取るので物理で動かないようにしておく
                rigidbody2D.bodyType = RigidbodyType2D.Static;
            }
            spriteRenderer = GetComponent<SpriteRenderer>();
            spriteRenderer.color = GetPlayerCharacterColor(ownerGameNo);
            pixelPerfectCamera = GameObject.FindObjectOfType<PixelPerfectCamera>();
            searchCollider.enabled = false;

            nameText.text = $"{ownerGameNo + 1}P";
            nameText.color = GetPlayerTextColor(ownerGameNo);
            namePlate.SetActive(!PhotonNetwork.OfflineMode);
        }

        /// <summary>
        /// アイテムを拾う
        /// </summary>
        /// <param name="item">対象アイテム</param>
        public void PickItem(DropItem item)
        {
            if (useItem == null)
            {
                useItem = item;
                photonView.RPC(nameof(RpcPickItem), RpcTarget.AllViaServer, item.id);
            }
        }

        /// <summary>
        /// RPC アイテムを拾う
        /// </summary>
        /// <param name="itemId">アイテムID</param>
        [PunRPC]
        private void RpcPickItem(int itemId)
        {
            var item = model.trashGenerator.GetItem(itemId);
            if (item != null)
            {
                // 先に誰かに拾われていないかを確認
                if (!item.IsPicked())
                {
                    // 拾う
                    item.PickItem(photonView.Owner.GetGameNo());

                    // アイテムをソケット位置にアタッチ
                    item.gameObject.transform.SetParent(socket.transform);
                    item.gameObject.transform.localPosition = Vector3.zero;
                    return;
                }
            }

            // 拾うのに失敗
            useItem = null;
        }

        /// <summary>
        /// アイテムを投げる
        /// </summary>
        /// <param name="force">投げる力</param>
        public void ThrowItem(Vector2 force)
        {
            if (useItem != null)
            {
                photonView.RPC(nameof(RpcThrowItem), RpcTarget.AllViaServer, useItem.id, useItem.transform.position, force.x, force.y, socket.transform.localPosition.y);
                useItem = null;
            }
        }

        /// <summary>
        /// RPC アイテムを投げる
        /// </summary>
        /// <param name="itemId">アイテムID</param>
        /// <param name="startPos">開始位置</param>
        /// <param name="forceX">X方向の投げる力</param>
        /// <param name="forceY">Y方向の投げる力</param>
        /// <param name="aboveGround">開始時の地面からの高さ</param>
        [PunRPC]
        private void RpcThrowItem(int itemId, Vector3 startPos, float forceX, float forceY, float aboveGround)
        {
            var item = model.trashGenerator.GetItem(itemId);
            if (item != null)
            {
                item.transform.position = startPos;
                item.ThrowItem(new Vector2(forceX, forceY), aboveGround);
            }
        }

        /// <summary>
        /// アイテムサーチコルーチン
        /// </summary>
        /// <param name="cancellationToken">キャンセルトークン</param>
        /// <returns>UniTaskVoid</returns>
        private async UniTaskVoid SearchCoroutine(CancellationToken cancellationToken)
        {
            // Rigidbodyが寝てると当たり判定が機能しないので起こす
            rigidbody2D.WakeUp();

            // 一定時間サーチ用コライダーをオンにする
            searchCollider.enabled = true;
            try
            {
                await UniTask.Delay(100, cancellationToken: cancellationToken);
            }
            finally
            {
                searchCollider.enabled = false;
            }
        }

        void IPunObservable.OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info)
        {
            if (stream.IsWriting)
            {
                // 値をストリームに書き込んで送信する
                stream.SendNext(spriteRenderer.flipX);
            }
            else
            {
                // 受信したストリームを読み込んで値を更新する
                spriteRenderer.flipX = (bool)stream.ReceiveNext();
            }
        }

        /// <summary>
        /// 他のコライダーと衝突した時
        /// </summary>
        /// <param name="collision">衝突相手のコリジョン</param>
        public void OnCollisionEnter2D(Collision2D collision)
        {
            // 衝突相手がキャラクターなら
            var targetChara = collision.gameObject.GetComponent<CharacterController2D>();
            if (targetChara != null)
            {
                // 相手がアイテムを投げだす
                targetChara.ThrowItem(Vector2.zero);
            }
        }
    }
}
