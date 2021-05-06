﻿using System;
using System.Collections;
using System.Collections.Generic;
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
        public Vector3 nextMoveCommand;
        public Animator animator;
        public bool flipX = false;
        public bool onFire = false;
        public bool releaseFire = false;
        public bool moveBrake = false;
        public Collider2D searchCollider;

        public GameObject socket;
        public UI.PowerGauge powerGauge;

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
        /// プレイヤー番号ごとの色を取得する
        /// </summary>
        /// <param name="playerId">プレイヤー番号</param>
        /// <returns>プレイヤー色</returns>
        public static Color GetPlayerColor(int playerId)
        {
            Color color;
            switch ((playerId - 1) % 4)
            {
                default:
                case 0:
                    color = Color.white;
                    break;
                case 1:
                    color = new Color(1.0f, 0.5f, 0.5f);
                    break;
                case 2:
                    color = new Color(0.5f, 1.0f, 0.5f);
                    break;
                case 3:
                    color = new Color(0.5f, 0.5f, 1.0f);
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
            velocity = Mathf.Clamp01(velocity + Time.deltaTime * acceleration);
            UpdateAnimator(nextMoveCommand, moveBrake);
            var move_command = (moveBrake) ? Vector3.zero : nextMoveCommand;
            float apply_speed = speed;
            if (useItem != null)
            {
                // アイテムの重さを見て減速
                apply_speed *= Mathf.Clamp(Mathf.Lerp(1.0f, 0.0f, useItem.data.mass / 5.0f), 0.1f, 1.0f);
            }
            rigidbody2D.velocity = Vector2.SmoothDamp(rigidbody2D.velocity, move_command * apply_speed, ref currentVelocity, acceleration, speed);
            spriteRenderer.flipX = (moveBrake ? nextMoveCommand.x : rigidbody2D.velocity.x) >= 0 ? true : false;
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

                // アイテムサーチ
                if (onFire)
                {
                    onFire = false;
                    StartCoroutine(SearchCoroutine());
                }
            }
            else
            {
                // パワーゲージの角度を移動入力の角度に合わせる
                float rad = Mathf.Atan2(nextMoveCommand.y, nextMoveCommand.x);
                powerGauge.transform.eulerAngles = new Vector3(0.0f, 0.0f, rad * Mathf.Rad2Deg);

                // ブレーキ中に移動入力があったら
                if (moveBrake)
                {
                    if (nextMoveCommand.magnitude != 0.0f)
                    {
                        // パワーゲージを表示
                        powerGauge.Show(0.0f);

                        // カメラのフォーカス対象がデフォルト状態なら入力方向にカメラを振る
                        if (model.cameraController.IsDefaultFocus())
                        {
                            model.cameraController.positionOffset = nextMoveCommand.normalized * 5.0f;
                        }
                    }
                }

                // チャージ中
                if (bCharge)
                {
                    chargeTime += Time.deltaTime;
                    if (chargeTime < 1.1f)
                    {
                        if (nextMoveCommand.magnitude != 0.0f)
                        {
                            powerGauge.Show((chargeTime > 1.0f) ? 1.0f : chargeTime / 1.0f);
                        }
                    }
                    else
                    {
                        bCharge = false;
                        if (nextMoveCommand.magnitude != 0.0f)
                        {
                            powerGauge.Show(0.0f);
                        }
                    }
                }

                // 攻撃ボタン押下
                if (onFire)
                {
                    onFire = false;

                    // チャージ開始
                    bCharge = true;
                    chargeTime = 0.0f;
                }

                // 攻撃ボタン離す
                if (releaseFire)
                {
                    releaseFire = false;

                    // チャージ中ならアイテム投射
                    if (bCharge)
                    {
                        bCharge = false;
                        Vector2 force = nextMoveCommand.normalized * chargeTime * 20.0f;
                        photonView.RPC(nameof(RpcThrowItem), RpcTarget.AllViaServer, useItem.id, useItem.transform.position, force.x, force.y, socket.transform.localPosition.y);
                        useItem = null;
                    }
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
            rigidbody2D = GetComponent<Rigidbody2D>();
            spriteRenderer = GetComponent<SpriteRenderer>();
            spriteRenderer.color = GetPlayerColor(photonView.OwnerActorNr);
            pixelPerfectCamera = GameObject.FindObjectOfType<PixelPerfectCamera>();
            searchCollider.enabled = false;
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
                    item.PickItem(photonView.OwnerActorNr);

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
        /// <returns>IEnumerator</returns>
        private IEnumerator SearchCoroutine()
        {
            // Rigidbodyが寝てると当たり判定が機能しないので起こす
            rigidbody2D.WakeUp();

            // 一定時間サーチ用コライダーをオンにする
            searchCollider.enabled = true;
            yield return new WaitForSeconds(0.1f);
            searchCollider.enabled = false;
            yield break;
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
                Debug.Log("mukimuki:" + spriteRenderer.flipX);
            }
        }
    }
}
