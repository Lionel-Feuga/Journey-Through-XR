using ObsidienneVR;
using UnityEngine;

namespace JourneyThroughXR.Activities
{
    /// <summary>
    /// Relie le sabre Obsidienne au reste de l'app : LaserSaber gère la saisie, la gâchette et les lames,
    /// ObsidienneController fait tourner le rotor. Ce script transmet du premier au second
    /// la main qui tient le sabre, l'état des lames et la pression longue qui déverrouille le rotor.
    /// Remplace l'adaptateur d'entrée livré avec le modèle (ObsidienneXRInput).
    /// </summary>
    [RequireComponent(typeof(LaserSaber))]
    [RequireComponent(typeof(ObsidienneController))]
    public class ObsidienneSaberLink : MonoBehaviour
    {
        private LaserSaber saber;
        private ObsidienneController rotor;
        private bool blade1On;
        private bool blade2On;

        private void Awake()
        {
            saber = GetComponent<LaserSaber>();
            rotor = GetComponent<ObsidienneController>();

            // Dès qu'une lame est allumée, la pression longue sert à déverrouiller le rotor, plus à allumer.
            saber.HoldReservedWhenIgnited = true;

            // Les lames sont affichées par LaserSaber : le contrôleur ne doit pas les activer ou les masquer lui-même.
            rotor.bladeA = null;
            rotor.bladeB = null;
        }

        private void Update()
        {
            Transform holder = saber.Holder;
            rotor.unlockHoldSeconds = Mathf.Max(0.1f, saber.HoldDuration);
            rotor.rotationSource = holder;
            rotor.SetTrackingActive(holder != null);

            if (saber.IsBlade1Ignited != blade1On)
            {
                blade1On = saber.IsBlade1Ignited;
                rotor.SetBladeA(blade1On);
            }
            if (saber.IsBlade2Ignited != blade2On)
            {
                blade2On = saber.IsBlade2Ignited;
                rotor.SetBladeB(blade2On);
            }

            // Seule une pression commencée lame allumée compte : celle qui sort les deux lames ne déverrouille pas dans la foulée.
            rotor.SetTriggerHeld(holder != null && saber.IsTriggerHeld && saber.PressStartedIgnited);
        }

        private void OnDisable()
        {
            if (rotor != null) rotor.SetTrackingActive(false);
        }
    }
}
