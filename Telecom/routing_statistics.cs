using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace σκοπός {
internal class RoutingStatistics : principia.ksp_plugin_adapter.SupervisedWindowRenderer {
  public RoutingStatistics(Telecom telecom) : base(telecom){
    Hide();
    telecom_ = telecom;
  }

  protected override string Title => "Σκοπός Telecom routing statistics";

  protected override void RenderWindowContents(int window_id) {
    if (!telecom_.enabled || telecom_.network == null) {
      UnityEngine.GUILayout.Label("Please wait for the Σκοπός Telecom network to initialize...");
      return;
    }
    using (new UnityEngine.GUILayout.HorizontalScope()) { // the most bootleg table imaginable
      using (new UnityEngine.GUILayout.VerticalScope()) {
        string[] labels = { "Routing Stats", "Precompute", "One-Hop", "Shortest Path", "A*", "Dijkstra's"};
        foreach (string label in labels) {
          using (new UnityEngine.GUILayout.HorizontalScope()) {
            UnityEngine.GUILayout.FlexibleSpace();
            UnityEngine.GUILayout.Label(label);
          }
        }
      }
      FixedUpdateMetric[] metrics = { 
        telecom_.network.routing_.heuristic.apsp_metric, 
        telecom_.network.routing_.one_hop_metric,
        telecom_.network.routing_.shortest_path_metric,
        telecom_.network.routing_.a_star_metric,
        telecom_.network.routing_.dijkstras_metric,
      };
      using (new UnityEngine.GUILayout.VerticalScope()) {
        UnityEngine.GUILayout.Label("Total Calls");
        foreach (FixedUpdateMetric metric in metrics) {
          UnityEngine.GUILayout.Label(metric.total_calls.ToString());
        }
      }
      using (new UnityEngine.GUILayout.VerticalScope()) {
        UnityEngine.GUILayout.Label("✓ Calls");
        foreach (FixedUpdateMetric metric in metrics) {
          UnityEngine.GUILayout.Label(metric.successes.ToString());
        }
      }
      using (new UnityEngine.GUILayout.VerticalScope()) {
        UnityEngine.GUILayout.Label("✗ Calls", principia.ksp_plugin_adapter.Style.Error(UnityEngine.GUI.skin.label));
        foreach (FixedUpdateMetric metric in metrics) {
          UnityEngine.GUILayout.Label(metric.failures.ToString());
        }
      }
      using (new UnityEngine.GUILayout.VerticalScope()) {
        UnityEngine.GUILayout.Label("Avg. Calls", principia.ksp_plugin_adapter.Style.Error(UnityEngine.GUI.skin.label));
        foreach (FixedUpdateMetric metric in metrics) {
          UnityEngine.GUILayout.Label($"{metric.average_calls_per_fixedupdate:F2}");
        }
      }


      using (new UnityEngine.GUILayout.VerticalScope()) {
        UnityEngine.GUILayout.Label("Avg. Time/Call");
        foreach (FixedUpdateMetric metric in metrics) {
          UnityEngine.GUILayout.Label($"{metric.average_runtime_per_call*1000:F2} ms");
        }
      }
      using (new UnityEngine.GUILayout.VerticalScope()) {
        UnityEngine.GUILayout.Label("Avg. Time Total");
        foreach (FixedUpdateMetric metric in metrics) {
          UnityEngine.GUILayout.Label($"{metric.average_runtime_per_fixedupdate*1000:F2} ms");
        }
      }
    }
    
    UnityEngine.GUI.DragWindow();
  }

  public void RenderButton() {
    if (UnityEngine.GUILayout.Button("Runtime Details…")) {
      Toggle();
    }
  }

  private Telecom telecom_;
}
}
