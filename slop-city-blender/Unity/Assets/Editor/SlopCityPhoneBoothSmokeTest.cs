#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;
namespace SlopCity.Editor
{
    /// <summary>Isolated offline booth geometry and bounded screen registration; no playback or saves.</summary>
    public static class SlopCityPhoneBoothSmokeTest
    {
        public static void Run()
        {
            if(!Application.isBatchMode||EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("A dedicated batch Edit Mode project is required.");
            int checks=0;void Check(bool value,string message){checks++;if(!value)throw new InvalidOperationException(message);}
            try
            {
                EditorSceneManager.OpenScene(SlopCitySceneBuilder.ScenePath,OpenSceneMode.Single);
                var player=Object.FindFirstObjectByType<SlopCityFirstPerson>();Check(player,"Saved player exists");player.enabled=false;
                foreach(var sync in Object.FindObjectsByType<SlopCityTikTokSync>(FindObjectsInactive.Include,FindObjectsSortMode.None))sync.enabled=false;
                var booths=SlopCityPhoneBooths.Ensure(player);Check(booths,"Street telephones install");
                Check(booths.transform.childCount==4,"Four established junction booths remain");
                foreach(Transform phone in booths.transform)
                {
                    Check(phone.Find("Domed_red_crown"),"Classic booth has a domed red roof");
                    Check(phone.GetComponentsInChildren<TextMesh>().Count(text=>text.text=="TELEPHONE")==4,"Every face has a TELEPHONE sign");
                    foreach(string face in new[]{"Front_glass_door","Rear_glass_panel","Left_glass_panel","Right_glass_panel"})
                    {var glass=phone.Find(face+"/Glass");Check(glass&&glass.GetComponent<Renderer>().sharedMaterial.color.a<.5f,"Framed glass is transparent");}
                    var finish=phone.GetComponent<SlopCitySalvageItem>();Check(finish&&finish.DustProne&&!finish.Pickable,"Old booth can be polished but never taken");
                    var tv=phone.GetComponentInChildren<SlopCityCityScreen>();Check(tv&&tv.Display&&tv.Channel==2,"Small booth TV uses the existing city screen pool");
                    Check(SlopCitySalvageItem.IsProtectedArt(tv.transform),"TV picture is protected from cleaning/taking");
                }
                Check(SlopCityPhoneBooths.Ensure(player)==booths,"Repeated installation does not duplicate booths or TVs");
                Debug.Log("SLOP_CITY_PHONE_BOOTHS_PASSED "+checks+" offline geometry checks; no playback or device acceptance.");
            }
            finally{EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);}
        }
    }
}
#endif
