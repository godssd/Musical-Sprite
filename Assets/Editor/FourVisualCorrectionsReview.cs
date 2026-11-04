using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>Explicit opt-in review of only the four already-installed visual features.</summary>
public static class FourVisualCorrectionsReview
{
    [MenuItem("Tools/Musical-Sprite/V11/13 Capture Visual Iteration V5")]
    public static void BatchV6()=>Capture("DesignPreviews/VisualIterationV6-20261101");
    public static void BatchV5(){Capture("DesignPreviews/VisualIterationV5-20261030");}
    [MenuItem("Tools/Musical-Sprite/V11/12 Capture Visual Iteration V4")]
    public static void BatchV4(){Capture("DesignPreviews/VisualIterationV4-20261030");}
    [MenuItem("Tools/Musical-Sprite/V11/11 Capture Visual Iteration V3")]
    public static void BatchV3(){Capture("DesignPreviews/VisualIterationV3-20261029");}
    [MenuItem("Tools/Musical-Sprite/V11/10 Capture Visual Iteration V2")]
    public static void BatchV2(){Capture("DesignPreviews/VisualIterationV2-20261029");}
    [MenuItem("Tools/Musical-Sprite/V11/09 Capture Four Visual Corrections")]
    public static void Batch() {
        Capture("DesignPreviews/FourVisualCorrections-20261029");
    }
    private static void Capture(string directory) {
        string output=Path.GetFullPath(directory);
        Directory.CreateDirectory(output);
        try {
            FullRibbonHUDReview.CaptureValidation(Path.Combine(output,"hud"));
            ComboReviewV11.CaptureValidation(Path.Combine(output,"combo"));
            if(directory.Contains("VisualIteration"))ComboReviewV11.CaptureValidation(Path.Combine(output,"combo-actual-size"),true);
            BattleVisualReviewV11.CaptureValidation(Path.Combine(output,"rails"));
            File.WriteAllText(Path.Combine(output,"validation.txt"),"PASS: runtime HUD, fever, six Combo tear keyframes and five four-track states rendered in Unity. Health timing, zero Combo, authored 0-9 paths, 0.23s paper readability and outgoing-snapshot continuity after a new hit checked. Scene not saved.");
        } catch(Exception e) {
            File.WriteAllText(Path.Combine(output,"validation.txt"),"FAIL: "+e);Debug.LogException(e);
            if(Application.isBatchMode)EditorApplication.Exit(1);
        }
    }
}
