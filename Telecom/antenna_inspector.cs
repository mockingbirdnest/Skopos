using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using principia.ksp_plugin_adapter;
using RealAntennas;
using static VehiclePhysics.Block;

namespace σκοπός {

internal class AntennaInspector : principia.ksp_plugin_adapter.SupervisedWindowRenderer {
  public AntennaInspector(Telecom telecom, RealAntennaDigital antenna)
    : base(telecom) {
      telecom_ = telecom;
      antenna_ = antenna;
  }

  protected override string Title => "Antenna inspector";

  protected override void RenderWindowContents(int window_id) {
    UnityEngine.GUILayout.Label(
        $"{antenna_.Name} on {antenna_.ParentNode.displayName}");
    var power = telecom_.network.routing_.usage.SourcedTxPowerUsage(antenna_);
    var spectrum = telecom_.network.routing_.usage.SourcedSpectrumUsage(
        antenna_);
    Style.HorizontalLine();
    UnityEngine.GUILayout.Label("Power usage:");
    foreach (var broadcast in power.usages) {
      if (broadcast.Length == 1) {
        UnityEngine.GUILayout.Label(
            $@"{antenna_.PowerDrawLinear * 1e-3 * broadcast[0].power
            :N1} W to {broadcast[0].link.link.rx.displayName} for {
            RATools.PrettyPrintDataRate(broadcast[0].link.connection.data_rate)} {
            broadcast[0].link.channel.links[0].tx.displayName}–{
            broadcast[0].link.channel.links.Last().rx.displayName}");
      } else {
        UnityEngine.GUILayout.Label(
            $@"{antenna_.PowerDrawLinear * 1e-3 * broadcast.Max(usage => usage.power)
            :N1} W to {string.Join(
                ", ",
                broadcast.Select(
                    usage => usage.link.link.rx.displayName).Distinct())} for {
                    RATools.PrettyPrintDataRate(
                        broadcast[0].link.connection.data_rate)} broadcast from {
            broadcast[0].link.channel.links[0].tx.displayName} to {string.Join(
                ", ",
              broadcast.Select(
                      usage => usage.link.channel.links.Last().rx.displayName))}");

      }
    }
    UnityEngine.GUI.DragWindow();
  }

  public void RenderButton() {
    if (UnityEngine.GUILayout.Button("Inspect…")) {
      Toggle();
    }
  }

  private Telecom telecom_;
  private RealAntennaDigital antenna_;
}

}
