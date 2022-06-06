using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ContractConfigurator;
using ContractConfigurator.Behaviour;

namespace σκοπός {
  public abstract class GroundSegmentMutationFactory : BehaviourFactory {
    private static readonly List<string> empty_ = new List<string>();
    public override bool Load(ConfigNode node) {
      var ok = base.Load(node);
      ok &= ConfigNodeUtil.ParseValue(node, "station", x => stations_ = x, this, empty_);
      ok &= ConfigNodeUtil.ParseValue(node, "customer", x => customers_ = x, this, empty_);
      ok &= ConfigNodeUtil.ParseValue(node, "connection", x => connections_ = x, this, empty_);
      var condition = ConfigNodeUtil.GetChildNode(node, "condition");
      ok &= ConfigNodeUtil.ParseValue<GroundSegmentMutation.State>(condition, "state", x => state_ = x, this);
      return ok;
    }
    public override ContractBehaviour Generate(ConfiguredContract contract) {
      return new GroundSegmentMutation(Operation(), stations_, customers_, connections_, state_);
    }

    protected abstract GroundSegmentMutation.Operation Operation();

    private List<string> stations_;
    private List<string> customers_;
    private List<string> connections_;
    private GroundSegmentMutation.State state_;
  }

  public class AddToGroundSegmentFactory : GroundSegmentMutationFactory {
    protected override GroundSegmentMutation.Operation Operation() {
      return GroundSegmentMutation.Operation.ADD;
    }
  }
  public class RemoveFromGroundSegmentFactory : GroundSegmentMutationFactory {
    protected override GroundSegmentMutation.Operation Operation() {
      return GroundSegmentMutation.Operation.REMOVE;
    }
  }

  public class GroundSegmentMutation : ContractBehaviour {
    public enum State {
      CONTRACT_OFFERED,
      CONTRACT_DECLINED,
    }

    public enum Operation {
      ADD,
      REMOVE,
    }

    public GroundSegmentMutation() {}

    public GroundSegmentMutation(Operation operation,
                                 List<string> stations,
                                 List<string> customers,
                                 List<string> connections,
                                 State state) {
      operation_ = operation;
      stations_ = stations;
      customers_ = customers;
      connections_ = connections;
      state_ = state;
    }

    protected override void OnLoad(ConfigNode node) {
      Enum.TryParse(node.GetValue("operation"), out operation_);
      stations_ = node.GetValuesList("station");
      customers_ = node.GetValuesList("customer");
      connections_ = node.GetValuesList("connection");
    }

    protected override void OnSave(ConfigNode node) {
      node.AddValue("operation", operation_);
      foreach (var station in stations_) {
        node.AddValue("station", station);
      }
      foreach (var customer in customers_) {
        node.AddValue("customer", customer);
      }
      foreach (var connection in connections_) {
        node.AddValue("connection", connection);
      }
    }

    protected override void OnOffered() {
      base.OnOffered();
      if (state_ == State.CONTRACT_OFFERED) {
        Behave();
      }
    }

    protected override void OnDeclined() {
      if (state_ == State.CONTRACT_DECLINED) {
        Behave();
      }
    }

    protected void Behave() {
      if (operation_ == Operation.ADD) {
        Telecom.Instance.network.AddStations(stations_);
        Telecom.Instance.network.AddCustomers(customers_);
        Telecom.Instance.network.AddConnections(connections_);
      } else {
        Telecom.Instance.network.RemoveStations(stations_);
        Telecom.Instance.network.RemoveCustomers(customers_);
        Telecom.Instance.network.RemoveConnections(connections_);
      }
    }

    private Operation operation_;
    private List<string> stations_;
    private List<string> customers_;
    private List<string> connections_;
    private State state_;
  }
}
