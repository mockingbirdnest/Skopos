namespace σκοπός {
    internal class RuntimeMetrics {
        public RuntimeMetrics() { }
        public int num_fixed_update_iterations_ = 0;
        public double fixed_update_runtime_ = 0;

        public double AverageFixedUpdateRuntime => fixed_update_runtime_ / num_fixed_update_iterations_;
    }
}
