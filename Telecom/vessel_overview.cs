using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommNet;
using CommNet.Network;
using RealAntennas;

namespace σκοπός {
  internal class VesselOverview : principia.ksp_plugin_adapter.SupervisedWindowRenderer {
    public VesselOverview(Telecom telecom) 
      : base(telecom) {
      telecom_ = telecom;
    }
    protected override string Title => "Σκοπός Telecom vessel overview";

    protected override void RenderWindowContents(int window_id) {
      var node_grouped_antennas = telecom_.network.routing_.usage.Users()
        .GroupBy(antenna => antenna.ParentNode)
        .Where(grouping => !(((RACommNode) grouping.Key).ParentVessel is null))
        .OrderBy(grouping => ((RACommNode) grouping.Key).ParentVessel.GetDisplayName());

      List<object> rows = new List<object>();

      double total_spectrum_usage = 0;
      double total_normalised_power_usage = 0;
       
      foreach (var antenna_group in node_grouped_antennas) {
        RACommNode node = (RACommNode) antenna_group.Key;
        rows.Add(node);
        bool res = false;
        if (!open_vessels_.TryGetValue(node, out res)) {
          open_vessels_[node] = false;
        }
        total_spectrum_usage += antenna_group.Select(antenna => telecom_.network.routing_.usage.SpectrumUsage(antenna)).Sum();
        total_normalised_power_usage += antenna_group.Select(antenna => telecom_.network.routing_.usage.TxPowerUsage(antenna)).Sum();
        if (res) {
          foreach (RealAntennaDigital antenna in antenna_group.OrderBy(antenna => $"{antenna.Name} -> {antenna.Target}")) {
            rows.Add(antenna);
          }
        }
      }

      using (new UnityEngine.GUILayout.HorizontalScope()) {
        UnityEngine.GUILayout.Label($"Total antenna spectrum usage: {RATools.PrettyPrint(total_spectrum_usage)}Hz");
        UnityEngine.GUILayout.Label($"Total (normalized) power usage: {total_normalised_power_usage:P2}");
        if (UnityEngine.GUILayout.Button(new UnityEngine.GUIContent("Reset Link Display", "Show all Skopos links, clearing any filter applied by the Show Links buttons."))) {
          telecom_.main_window_.focused_vessel = null;
        }
      }
      
      using (new UnityEngine.GUILayout.HorizontalScope()) { // Yet another budget table. Yippee.
        using (new UnityEngine.GUILayout.VerticalScope()) { // name
          foreach (var row in rows) {
            using (new UnityEngine.GUILayout.HorizontalScope()) {
              if (row is RACommNode node) {
                if (UnityEngine.GUILayout.Button(
                    open_vessels_[node] ? "−" : "+", GUILayoutWidth(1))) {
                  open_vessels_[node] = !open_vessels_[node];
                  ScheduleShrink();
                  return;
                }
                string name = node.displayName;
                if (row == telecom_.main_window_.focused_vessel) {
                  name = $">> {node.displayName} <<";
                }
                UnityEngine.GUILayout.Label($"{name}");
              } else if (row is RealAntennaDigital antenna) {
                UnityEngine.GUILayout.Label($"{antenna.Name} -> {antenna.Target}");
              }
            }
          }
        }
        using (new UnityEngine.GUILayout.VerticalScope()) { // used spectrum
          foreach (var row in rows) {
            using (new UnityEngine.GUILayout.HorizontalScope()) {
              if (row is RACommNode node) {
                UnityEngine.GUILayout.Label($" "); // Padding space.
              } else if (row is RealAntennaDigital antenna) {
                UnityEngine.GUILayout.FlexibleSpace();
                UnityEngine.GUILayout.Label($"{RATools.PrettyPrint(telecom_.network.routing_.usage.SpectrumUsage(antenna))}Hz /");
              }
            }
          }
        }
        using (new UnityEngine.GUILayout.VerticalScope()) { // total spectrum
          foreach (var row in rows) {
            using (new UnityEngine.GUILayout.HorizontalScope()) {
              if (row is RACommNode node) {
                UnityEngine.GUILayout.Label($" "); // Padding space.
              } else if (row is RealAntennaDigital antenna) {
                UnityEngine.GUILayout.FlexibleSpace();
                UnityEngine.GUILayout.Label($"{RATools.PrettyPrint(antenna.RFBand.ChannelWidth)}Hz");
              }
            }
          }
        }
        using (new UnityEngine.GUILayout.VerticalScope()) { // used power
          foreach (var row in rows) {
            using (new UnityEngine.GUILayout.HorizontalScope()) {
              if (row is RACommNode node) {
                UnityEngine.GUILayout.Label($" "); // Padding space.
              } else if (row is RealAntennaDigital antenna) {
                UnityEngine.GUILayout.FlexibleSpace();
                UnityEngine.GUILayout.Label($"{RATools.PrettyPrint(antenna.PowerDrawLinear * 1e-3 * telecom_.network.routing_.usage.TxPowerUsage(antenna))}W /");
              }
            }
          }
        }
        using (new UnityEngine.GUILayout.VerticalScope()) { // total power
          foreach (var row in rows) {
            using (new UnityEngine.GUILayout.HorizontalScope()) {
              if (row is RACommNode node) {
                UnityEngine.GUILayout.Label($" "); // Padding space.
              } else if (row is RealAntennaDigital antenna) {
                UnityEngine.GUILayout.FlexibleSpace();
                UnityEngine.GUILayout.Label($"{RATools.PrettyPrint(antenna.PowerDrawLinear * 1e-3)}W");
              }
            }
          }
        }
        using (new UnityEngine.GUILayout.VerticalScope()) { // button for antennas
          foreach (var row in rows) {
            using (new UnityEngine.GUILayout.HorizontalScope()) {
              if (row is RACommNode node) {
                if (UnityEngine.GUILayout.Button(new UnityEngine.GUIContent("Show Links", "Shows only links with one end at this vessel. No effect unless Show network is ticked."))) {
                  telecom_.main_window_.focused_vessel = node;
                }
              } else if (row is RealAntennaDigital antenna) {
                if (!telecom_.main_window_.antenna_inspectors.TryGetValue(
                      antenna, out var inspector)) {
                  inspector = new AntennaInspector(telecom_, antenna);
                  telecom_.main_window_.antenna_inspectors[antenna] = inspector;
                }
                inspector.RenderButton();
              }
            }
          }
        }
      }
      
      UnityEngine.GUI.DragWindow();
    }

    public void RenderButton() {
      if (UnityEngine.GUILayout.Button("Vessel Overview")) {
        Toggle();
      }
    }

    private Telecom telecom_;
    private readonly Dictionary<RACommNode, bool> open_vessels_ = new Dictionary<RACommNode, bool>();
  }
}
