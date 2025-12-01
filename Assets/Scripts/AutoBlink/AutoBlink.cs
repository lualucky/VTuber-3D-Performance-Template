//
//AutoBlink.cs
//オート目パチスクリプト
//2014/06/23 N.Kobayashi
//
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
// using System.Security.Policy;

namespace UnityChan
{
    public class AutoBlink : MonoBehaviour
	{
        public bool isActive = true;                //オート目パチ有効

        private bool eyesClosed = false;
        private float eyesClosedTransitionsTime = .1f;
        private float smileValue = 0;

        float eyesT;

        public bool useIndividualEyeblink;
        public int blinkBlendshape;
        public int rightBlinkBlendshape;
        public int leftBlinkBlendshape;
        public int[] exclusionShapes;
        private int[] exclusionidx;
        public SkinnedMeshRenderer Head;
        public float ratio_Close = 100.0f;           //閉じ目ブレンドシェイプ比率
        public float ratio_HalfClose = 20.0f;       //半閉じ目ブレンドシェイプ比率

        [HideInInspector]
        public float
            ratio_Open = 0.0f;
        private bool isBlink = false;               //目パチ管理用

        public float timeBlink = 0.2f;              //目パチの時間
        private float blinkT = 0.0f;          //タイマー残り時間

        public float threshold = 0.3f;              // ランダム判定の閾値
        public float interval = 3.0f;               // ランダム判定のインターバル
        float intervalT = 0f;

        bool ignoreDefaultShapes = false;

        enum Status
        {
            Close,
            HalfClose,
            Open,
            ForcedClosed
        }

        private Status eyeStatus = Status.Open;   //現在の目パチステータス

        void Awake ()
		{
			if (Head == null)
			{
                for (int i = 0; i < transform.childCount && Head == null; ++i)
                {
                    SkinnedMeshRenderer mesh = transform.GetChild(i).gameObject.GetComponent<SkinnedMeshRenderer>();
                    if (mesh.sharedMesh.blendShapeCount > 0)
                    {
                        Head = mesh;
                    }
                }
            }

            exclusionidx = new int[exclusionShapes.Length];
            for (int i = 0; i < exclusionShapes.Length; ++i)
            {
                exclusionidx[i] = exclusionShapes[i];
            }

            intervalT = interval;
        }

        private void OnValidate()
        {
            Awake();
        }

        // Update is called once per frame
        void Update()
        {
            if (isActive)
            {
                intervalT -= Time.deltaTime;

                if (isBlink)
                    blinkT -= Time.deltaTime;

                if (intervalT <= 0)
                {
                    float _seed = Random.Range(0.0f, 1.0f);
                    bool breakout = false;

                    if (!isBlink && eyeStatus == Status.Open && _seed > threshold)
                    {
                        // -- check if any exclusion shapes are active
                        for (int i = 0; i < exclusionShapes.Length; ++i)
                        {
                            if (exclusionidx[i] >= 0)
                            {
                                if (Head.GetBlendShapeWeight(exclusionidx[i]) > 5f)
                                {
                                    breakout = true;
                                    break;
                                }
                            }
                        }

                        if (!breakout && eyeStatus == Status.Open)
                        {
                            isBlink = true;
                            blinkT = timeBlink;
                        }
                    }

                    intervalT = interval;
                }
            }
        }

        void LateUpdate()
        {
            if (!isActive)
            {
                return;
            }

            if (isBlink)
            {
                if (blinkT >= timeBlink - (timeBlink * .15f))
                {
                    SetHalfCloseEyes();
                }
                if (blinkT >= timeBlink - (timeBlink * .7f))
                {
                    SetCloseEyes();
                }
                else if (blinkT >= 0)
                {
                    SetHalfCloseEyes();
                }
                else
                {
                    SetOpenEyes();
                    isBlink = false;
                }
            }

            if (eyesClosed)
            {
                if (eyeStatus != Status.ForcedClosed)
                {
                    if (Application.isPlaying)
                        eyesT = eyesClosedTransitionsTime;
                    else
                        eyesT = 0f;
                }

                eyeStatus = Status.ForcedClosed;

                if (eyesT > 0)
                {
                    eyesT -= Time.deltaTime;
                    if (eyesT < 0)
                        eyesT = 0f;
                }
                SetEyeValue((1.0f - (eyesT / eyesClosedTransitionsTime)) * 100);
            }
            else if (!eyesClosed && eyeStatus == Status.ForcedClosed)
            {
                if (eyesT == 0)
                {
                    if (Application.isPlaying)
                        eyesT = eyesClosedTransitionsTime * 2f;
                    else
                        eyesT = 0f;
                }

                if (eyesT > 0.0f)
                {
                    eyesT -= Time.deltaTime;
                    if (eyesT < 0f)
                    {
                        eyesT = 0f;
                        eyeStatus = Status.Open;
                    }
                }

                SetEyeValue((eyesT / (eyesClosedTransitionsTime * 2f)) * 100);
            }
        }

        void SetEyeValue(float v)
        {
            if (ignoreDefaultShapes)
                SetNormalEyesValue(v);
            else
                SetNormalEyesValue(v * (1f - smileValue));
        }

        void SetNormalEyesValue(float v)
        {
            if (useIndividualEyeblink)
            {
                Head.SetBlendShapeWeight(leftBlinkBlendshape, v);
                Head.SetBlendShapeWeight(rightBlinkBlendshape, v);
            }
            else
            {
                Head.SetBlendShapeWeight(blinkBlendshape, v);
            }
        }

        // Use this for initialization
        /*void Start ()
		{
			ResetTimer ();
			// ランダム判定用関数をスタートする
			StartCoroutine ("RandomChange");
		}

		//タイマーリセット
		void ResetTimer ()
		{
			timeRemining = timeBlink;
			timerStarted = false;
		}

		// Update is called once per frame
		void Update ()
		{
			if (!timerStarted) {
				eyeStatus = Status.Close;
				timerStarted = true;
			}
			if (timerStarted) {
				timeRemining -= Time.deltaTime;
				if (timeRemining <= 0.0f) {
					eyeStatus = Status.Open;
					ResetTimer ();
				} else if (timeRemining <= timeBlink * 0.3f) {
					eyeStatus = Status.HalfClose;
				}
			}
		}

		void LateUpdate ()
		{
			if (isActive) {
				if (isBlink) {
					switch (eyeStatus) {
					case Status.Close:
						SetCloseEyes ();
						break;
					case Status.HalfClose:
						SetHalfCloseEyes ();
						break;
					case Status.Open:
						SetOpenEyes ();
						isBlink = false;
						break;
					}
				}
			}
		}
		
		 // ランダム判定用関数
		IEnumerator RandomChange ()
		{
			// 無限ループ開始
			while (true) {
				//ランダム判定用シード発生
				float _seed = Random.Range (0.0f, 1.0f);
				if (!isBlink) {
					if (_seed > threshold) {
						isBlink = true;
					}
				}
				// 次の判定までインターバルを置く
				yield return new WaitForSeconds (interval);
			}
		}
		 */

        void SetCloseEyes ()
		{
            if (useIndividualEyeblink)
            {
                Head.SetBlendShapeWeight(leftBlinkBlendshape, ratio_Close);
                Head.SetBlendShapeWeight(rightBlinkBlendshape, ratio_Close);
            }
            else
            {
                Head.SetBlendShapeWeight(blinkBlendshape, ratio_Close);
            }
        }

		void SetHalfCloseEyes ()
		{
            if (useIndividualEyeblink)
            {
                Head.SetBlendShapeWeight(leftBlinkBlendshape, ratio_HalfClose);
                Head.SetBlendShapeWeight(rightBlinkBlendshape, ratio_HalfClose);
            }
            else
            {
                Head.SetBlendShapeWeight(blinkBlendshape, ratio_HalfClose);
            }
        }

		void SetOpenEyes ()
		{
			if(useIndividualEyeblink)
			{
                Head.SetBlendShapeWeight(leftBlinkBlendshape, ratio_Open);
                Head.SetBlendShapeWeight(rightBlinkBlendshape, ratio_Open);
            }
			else
			{
                Head.SetBlendShapeWeight(blinkBlendshape, ratio_Open);
            }
		}
	}
}