﻿using System;
using System.Collections.Generic;
using System.Linq;
using KerbalConstructionTime;

namespace σκοπός {
  [KSPScenario(
    ScenarioCreationOptions.AddToAllGames,
    new[] { GameScenes.SPACECENTER, GameScenes.TRACKSTATION, GameScenes.FLIGHT, GameScenes.EDITOR })]
  public sealed class Unlocker : ScenarioModule {
    public override void OnLoad(ConfigNode node) {
      base.OnLoad(node);
      foreach (ConfigNode periods in
                GameDatabase.Instance.GetConfigNodes("KCT_TECH_NODE_PERIODS")) {
        foreach (ConfigNode tech_node in periods.GetNodes("TECHNode")) {
          var year = int.Parse(tech_node.GetValue("startYear"));
          if (!techs_by_year.ContainsKey(year)) {
            techs_by_year[year] = new List<string>();
          }
          techs_by_year[year].Add(tech_node.GetValue("id"));
        }
      }
    }

    private DateTime start_of_campaign = new DateTime(1962, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private void FixedUpdate() {
      DateTime now = RSS.current_time;
      Funding.Instance.SetFunds(Math.Max(1e12, Funding.Instance.Funds), TransactionReasons.Cheating);
      if (now < start_of_campaign) {
        var next_week = now.AddDays(7);
        var t = next_week < start_of_campaign ? next_week : start_of_campaign;
        Planetarium.SetUniversalTime((t - RSS.epoch).TotalSeconds);
      }
      bool facilities_maxed = true;
      foreach (var upgradeable in ScenarioUpgradeableFacilities.protoUpgradeables.Values) {
        foreach (var facility in upgradeable.facilityRefs) {
          if (!facility.id.Contains("LaunchPad") &&
              facility.FacilityLevel != facility.MaxLevel) {
            facilities_maxed = false;
          }
        }
      }
      if (!facilities_maxed) {
        ScenarioUpgradeableFacilities.Instance.CheatFacilities();
        RealAntennas.RACommNetScenario.GroundStationTechLevel = RealAntennas.RACommNetScenario.MaxTL;
      }

      PresetManager.Instance.ActivePreset.GeneralSettings.Enabled = false;

      if (now.Year > current_year) {
        current_year = now.Year;

        UnityEngine.Debug.Log($"UNLOCKING TECHS UP TO {current_year}");
        foreach (var year_techs in techs_by_year) {
          int year = year_techs.Key;
          List<string> techs = year_techs.Value;
          if (year > current_year) {
            UnityEngine.Debug.Log("DONE");
            return;
          }
          UnityEngine.Debug.Log($"UNLOCKING {year} TECHS");
          foreach (string tech in techs) {
            UnityEngine.Debug.Log($"UNLOCKING {tech}");
            ProtoTechNode node = new ProtoTechNode {
              techID = tech,
              state = RDTech.State.Available,
              scienceCost = 1729,
              partsPurchased =
                  PartLoader.Instance.loadedParts.Where(p => p.TechRequired == tech).ToList()
            };
            ResearchAndDevelopment.Instance.SetTechState(tech, node);
            foreach (var upgrade in PartUpgradeManager.Handler.GetUpgradesForTech(tech)) {
              UnityEngine.Debug.Log($"UNLOCKING {upgrade.name}");
              PartUpgradeManager.Handler.SetUnlocked(upgrade.name, true);
            }
          }
        }
      }
    }

    private readonly SortedDictionary<int, List<string>> techs_by_year =
        new SortedDictionary<int, List<string>>();
    private int current_year;
  }
}
