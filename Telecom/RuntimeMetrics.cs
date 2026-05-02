namespace σκοπός {
    internal class RuntimeMetrics {
        public RuntimeMetrics() { }
        public int num_iterations_ = 0;
        public double total_runtime_ = 0;

        public double AverageRefreshRuntime => total_runtime_ / num_iterations_;
    }
}
