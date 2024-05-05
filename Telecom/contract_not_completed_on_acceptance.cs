using ContractConfigurator;

namespace σκοπός {

  // Same as CompleteContract, except it is inverted, and it does not checkOnActiveContract.
  public class ContractNotCompletedOnAcceptance : CompleteContractRequirement  {
    public ContractNotCompletedOnAcceptance() {}
    public override bool LoadFromConfig(ConfigNode configNode) {
      bool valid = base.LoadFromConfig(configNode);
      checkOnActiveContract = false;
      invertRequirement = true;
      return valid;
    }
  }

}
