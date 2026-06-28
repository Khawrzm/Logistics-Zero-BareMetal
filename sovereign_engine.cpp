#include <span>
#include <atomic>
#include <thread>
#include <vector>
#include <iostream>
#include <cstdint>
#include <numeric>

#ifdef _WIN32
#define EXPORT __declspec(dllexport)
#else
#define EXPORT __attribute__((visibility("default")))
#endif

enum CellState : uint32_t {
    STATE_DIRTY = 0,
    STATE_COMPUTING = 1,
    STATE_UP_TO_DATE = 2,
    STATE_CYCLE = 3
};

struct Cell {
    int id;
    double value;
    int formula_type; // 0: Raw value, 1: SUM, 2: AVERAGE
    int dep_start;    // Index in the dependencies array
    int dep_count;    // Number of dependencies
    uint64_t sync_state; // Upper 32-bits: Thread ID, Lower 32-bits: CellState
};

// Hash function to get a unique 32-bit ID for a thread
uint32_t GetCurrentThreadIdHash() {
    auto id = std::this_thread::get_id();
    return std::hash<std::thread::id>{}(id) & 0xFFFFFFFF;
}

// Recursive cell evaluation with speculative reevaluation & CAS tie-breaker
bool EvaluateCell(
    int cell_idx,
    std::span<Cell> cells,
    std::span<const int> dep_indices,
    uint32_t thread_hash
) {
    Cell& cell = cells[cell_idx];
    auto state_ref = std::atomic_ref<uint64_t>(cell.sync_state);

    while (true) {
        uint64_t current = state_ref.load(std::memory_order_relaxed);
        uint32_t owner = static_cast<uint32_t>(current >> 32);
        CellState state = static_cast<CellState>(current & 0xFFFFFFFF);

        if (state == STATE_UP_TO_DATE) {
            return true;
        }

        if (state == STATE_CYCLE) {
            return false; // Cycle detected
        }

        if (state == STATE_COMPUTING) {
            if (owner == thread_hash) {
                // Cycle detected: The current thread reached this cell again
                uint64_t cycle_desired = (static_cast<uint64_t>(thread_hash) << 32) | STATE_CYCLE;
                state_ref.compare_exchange_strong(current, cycle_desired);
                return false;
            }

            // Cyclic dependency resolve without locks: Speculative Reevaluation with Thread ID as Tie-Breaker
            if (thread_hash > owner) {
                // Speculative takeover: Attempt to take over computation from lower-priority thread
                uint64_t takeover_desired = (static_cast<uint64_t>(thread_hash) << 32) | STATE_COMPUTING;
                if (state_ref.compare_exchange_strong(current, takeover_desired)) {
                    // Takeover successful! Fall through to compute.
                } else {
                    continue; // State changed, retry
                }
            } else {
                // Thread hash is lower: back off to let the higher thread finish or wait speculatively
                std::this_thread::yield();
                // To prevent deadlock, if wait takes too long, we read the current value speculatively
                return true; 
            }
        }

        if (state == STATE_DIRTY) {
            uint64_t expected = current;
            uint64_t desired = (static_cast<uint64_t>(thread_hash) << 32) | STATE_COMPUTING;
            if (state_ref.compare_exchange_strong(expected, desired)) {
                break; // We own the computation now
            }
        }
    }

    // Zero-copy access to dependencies of this cell using std::span
    std::span<const int> cell_deps = dep_indices.subspan(cell.dep_start, cell.dep_count);
    
    // Evaluate all dependencies
    bool success = true;
    double sum = 0.0;
    int count = 0;

    for (int dep_idx : cell_deps) {
        if (dep_idx >= 0 && dep_idx < static_cast<int>(cells.size())) {
            if (!EvaluateCell(dep_idx, cells, dep_indices, thread_hash)) {
                success = false;
            }
            sum += cells[dep_idx].value;
            count++;
        }
    }

    // Compute final value based on formula type
    if (success) {
        if (cell.formula_type == 1) { // SUM
            cell.value = sum;
        } else if (cell.formula_type == 2) { // AVERAGE
            cell.value = (count > 0) ? (sum / count) : 0.0;
        }
        // Write back clean state
        uint64_t final_state = (static_cast<uint64_t>(thread_hash) << 32) | STATE_UP_TO_DATE;
        state_ref.store(final_state, std::memory_order_release);
        return true;
    } else {
        uint64_t error_state = (static_cast<uint64_t>(thread_hash) << 32) | STATE_CYCLE;
        state_ref.store(error_state, std::memory_order_release);
        return false;
    }
}

extern "C" {
    // Exported function for C# FFI Bridge
    EXPORT void RecalculateEngine(
        Cell* cells_ptr,
        int cells_count,
        const int* dep_indices_ptr,
        int dep_indices_count,
        int thread_count
    ) {
        // Zero-copy wrapping of input arrays using std::span
        std::span<Cell> cells(cells_ptr, cells_count);
        std::span<const int> dep_indices(dep_indices_ptr, dep_indices_count);

        if (thread_count <= 1) {
            uint32_t thread_hash = GetCurrentThreadIdHash();
            for (int i = 0; i < cells_count; ++i) {
                EvaluateCell(i, cells, dep_indices, thread_hash);
            }
        } else {
            // Multi-threaded speculative execution
            std::vector<std::thread> workers;
            for (int t = 0; t < thread_count; ++t) {
                workers.emplace_back([cells, dep_indices, cells_count, t]() {
                    uint32_t thread_hash = GetCurrentThreadIdHash() + t; // unique offset
                    for (int i = 0; i < cells_count; ++i) {
                        EvaluateCell(i, cells, dep_indices, thread_hash);
                    }
                });
            }
            for (auto& worker : workers) {
                if (worker.joinable()) {
                    worker.join();
                }
            }
        }
    }
}
