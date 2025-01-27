﻿using Cysharp.Threading.Tasks;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace RPGM.UI
{
    /// <summary>
    /// フェードUI
    /// とりあえずシングルトン
    /// </summary>
    public class FadeScreen : MonoBehaviour
    {
        public UnityEngine.UI.Image fade;

        static FadeScreen instance;

        float startAlpha;
        float endAlpha;
        float time;
        Color color;

        void Awake()
        {
            instance = this;
            fade.enabled = false;
            time = -1.0f;
        }

        private void Update()
        {
            if (time > 0.0f)
            {
                // フェード開始
                instance.FadeCoroutine(startAlpha, endAlpha, time, color, this.GetCancellationTokenOnDestroy()).Forget();
                time = -1.0f;
            }
        }

        /// <summary>
        /// フェード制御コルーチン
        /// </summary>
        /// <param name="startAlpha">開始アルファ値</param>
        /// <param name="endAlpha">終了アルファ値</param>
        /// <param name="time">フェード時間</param>
        /// <param name="cancellationToken">キャンセルトークン</param>
        /// <returns>UniTask</returns>
        private async UniTask FadeCoroutine(float startAlpha, float endAlpha, float time, Color color, CancellationToken cancellationToken)
        {
            float elapsed_time = 0.0f;
            fade.enabled = true;

            // フェード時間をかけて線形補間
            try
            {
                while (elapsed_time < time)
                {
                    color.a = Mathf.Lerp(startAlpha, endAlpha, elapsed_time / time);
                    fade.color = color;
                    await UniTask.Yield(cancellationToken);
                    elapsed_time += Time.deltaTime;
                }
            }
            finally
            {
                // フェード終了
                if (endAlpha > 0.0f)
                {
                    color.a = endAlpha;
                    fade.color = color;
                }
                else
                {
                    fade.enabled = false;
                }
            }
        }

        /// <summary>
        /// フェードイン開始
        /// </summary>
        /// <param name="time">フェード時間</param>
        /// <param name="color">フェード色</param>
        public static void FadeIn(float time, Color color)
        {
            instance.startAlpha = 1.0f;
            instance.endAlpha = 0.0f;
            instance.time = time;
            instance.color = color;
        }

        /// <summary>
        /// フェードアウト開始
        /// </summary>
        /// <param name="time">フェード時間</param>
        /// <param name="color">フェード色</param>
        public static void FadeOut(float time, Color color)
        {
            instance.startAlpha = 0.0f;
            instance.endAlpha = 1.0f;
            instance.time = time;
            instance.color = color;
        }
    }
}
