using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ContractConfigurator;
using ContractConfigurator.Behaviour;

namespace σκοπός {
  public abstract class ConnectionActivationFactory : BehaviourFactory {
    private static readonly List<string> empty_ = new List<string>();
    public override bool Load(ConfigNode node) {
      var ok = base.Load(node);
      ok &= ConfigNodeUtil.ParseValue(node, "connection", x => connections_ = x, this, empty_);
      var condition = ConfigNodeUtil.GetChildNode(node, "condition");
      ok &= ConfigNodeUtil.ParseValue<TriggeredBehaviour.State>(condition, "state", x => state_ = x, this);
      ok &= ConfigNodeUtil.ParseValue(condition, "parameter", x => parameters_ = x, this, empty_);
      return ok;
    }
    public override ContractBehaviour Generate(ConfiguredContract contract) {
      return new ConnectionActivation(connections_, Operation(), state_, parameters_);
    }

    public abstract ConnectionActivation.Operation Operation();

    private List<string> connections_;
    private List<string> parameters_;
    TriggeredBehaviour.State state_;
  }

  public class ActivateConnectionFactory : ConnectionActivationFactory {
    public override ConnectionActivation.Operation Operation() {
      return ConnectionActivation.Operation.ACTIVATE;
    }
  }

  public class DeactivateConnectionFactory : ConnectionActivationFactory {
    public override ConnectionActivation.Operation Operation() {
      return ConnectionActivation.Operation.DEACTIVATE;
    }
  }


  public class ConnectionActivation : TriggeredBehaviour {
    public enum Operation {
      ACTIVATE,
      DEACTIVATE,
    }

    public ConnectionActivation() { }

    public ConnectionActivation(
        List<string> connections,
        Operation operation,
        State state,
        List<string> parameters)
      : base(state, parameters) {
      operation_ = operation;
      connections_ = connections;
    }

    protected override void OnLoad(ConfigNode node) {
      base.OnLoad(node);
      Enum.TryParse(node.GetValue("operation"), out operation_);
      connections_ = node.GetValuesList("connection");
    }

    protected override void OnSave(ConfigNode node) {
      base.OnSave(node);
      node.AddValue("operation", operation_);
      foreach (var connection in connections_) {
        node.AddValue("connection", connection);
      }
    }

    protected override void TriggerAction() {
      foreach (var connection in connections_) {
        switch (operation_) {
          case Operation.ACTIVATE:
            Telecom.Instance.network.GetConnection(connection).Activate();
            break;
          case Operation.DEACTIVATE:
            Telecom.Instance.network.GetConnection(connection).Deactivate();
            break;
        }
      }
    }

    private List<string> connections_;
    private Operation operation_;
  }
}
