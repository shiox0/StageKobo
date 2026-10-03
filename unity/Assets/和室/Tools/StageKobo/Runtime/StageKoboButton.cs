#if STAGEKOBO_UDON
// すてーじ工房：リモコンの1ボタン（UdonSharp 版）
// ・ワールドの UI リモコン：Button の OnClick から UdonBehaviour.SendCustomEvent("Press") で呼ばれる（インポーターが設定）
// ・ステージ裏の卓（物理スイッチ）：クリック／レーザーの Interact で呼ばれる。押すとボタンが沈む（卓のマテリアルの _PressId / _PressT）
using UdonSharp;
using UnityEngine;

namespace Washitsu.StageKobo
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class StageKoboButton : UdonSharpBehaviour
    {
        public StageKoboController controller;
        public int ch;    // チャンネル番号（StageKoboController.CH_～）または動作番号（ACT_～ / StageKoboVJ の V_～・M_～）
        public int val;   // 選ぶステートの番号など
        public StageKoboVJ vj;          // VJ 卓のボタン（ch は StageKoboVJ の動作番号）
        public Material deskMat;        // 卓のボタン：押したら沈ませる
        public int slot;                // 卓の中のボタン番号

        public void Press()
        {
            if (vj != null) vj.Press(ch, val);
            else if (controller != null) controller.Press(ch, val);
            if (deskMat != null)
            {
                deskMat.SetFloat("_PressId", slot);
                deskMat.SetFloat("_PressT", Time.timeSinceLevelLoad);
            }
        }

        public override void Interact()
        {
            Press();
        }
    }
}
#endif
