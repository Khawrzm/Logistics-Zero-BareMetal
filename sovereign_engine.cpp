#include <span>
#include <atomic>
#include <thread>
#include <vector>
#include <iostream>
#include <cstdint>
#include <cmath>
#include <numeric>

#ifdef _WIN32
#define EXPORT __declspec(dllexport)
#else
#define EXPORT __attribute__((visibility("default")))
#endif

// Operation codes for formula AST evaluation
enum OpType : uint8_t {
    OP_CONST = 0,
    OP_CELL_REF = 1,
    OP_ADD = 2,
    OP_SUB = 3,
    OP_MUL = 4,
    OP_DIV = 5,
    OP_SUM = 6,
    OP_AVG = 7
};

// Blittable AST node layout
struct ExpressionNode {
    OpType type;
    double const_value;
    int cell_ref_idx;
    int range_start_idx;
    int range_end_idx;
};

// Blittable Cell node layout containing evaluation state
struct Cell {
    int id;
    double value;
    int expr_start; // Index in the global expression nodes span
    int expr_count; // Number of expression nodes belonging to this cell
    uint64_t sync_state; // Upper 32 bits: Owner Thread ID, Lower 32 bits: CellState
};

enum CellState : uint32_t {
    STATE_DIRTY = 0,
    STATE_COMPUTING = 1,
    STATE_UP_TO_DATE = 2,
    STATE_CYCLE = 3
};

// Hashed thread ID generation for tie-breaking
uint32_t GetCurrentThreadIdHash() {
    auto id = std::this_thread::get_id();
    return std::hash<std::thread::id>{}(id) & 0xFFFFFFFF;
}

// Forward declaration of AST evaluator
bool EvaluateAST(
    int cell_idx,
    std::span<Cell> cells,
    std::span<const ExpressionNode> expr_nodes,
    uint32_t thread_hash
);

// Evaluates an individual AST node for a cell
double EvaluateNode(
    const ExpressionNode& node,
    std::span<Cell> cells,
    std::span<const ExpressionNode> expr_nodes,
    uint32_t thread_hash,
    bool& success
) {
    switch (node.type) {
        case OP_CONST:
            return node.const_value;
            
        case OP_CELL_REF:
            if (node.cell_ref_idx >= 0 && node.cell_ref_idx < static_cast<int>(cells.size())) {
                if (!EvaluateAST(node.cell_ref_idx, cells, expr_nodes, thread_hash)) {
                    success = false;
                }
                return cells[node.cell_ref_idx].value;
            }
            success = false;
            return 0.0;
            
        case OP_SUM: {
            double sum = 0.0;
            for (int i = node.range_start_idx; i <= node.range_end_idx; ++i) {
                if (i >= 0 && i < static_cast<int>(cells.size())) {
                    if (!EvaluateAST(i, cells, expr_nodes, thread_hash)) {
                        success = false;
                    }
                    sum += cells[i].value;
                }
            }
            return sum;
        }
        
        case OP_AVG: {
            double sum = 0.0;
            int count = 0;
            for (int i = node.range_start_idx; i <= node.range_end_idx; ++i) {
                if (i >= 0 && i < static_cast<int>(cells.size())) {
                    if (!EvaluateAST(i, cells, expr_nodes, thread_hash)) {
                        success = false;
                    }
                    sum += cells[i].value;
                    count++;
                }
            }
            return (count > 0) ? (sum / count) : 0.0;
        }
        
        default:
            success = false;
            return 0.0;
    }
}

// Lock-free cell evaluation with speculative reevaluation & Thread-ID tie-breaking
bool EvaluateAST(
    int cell_idx,
    std::span<Cell> cells,
    std::span<const ExpressionNode> expr_nodes,
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
            return false;
        }

        if (state == STATE_COMPUTING) {
            if (owner == thread_hash) {
                // Cycle detected in current thread's dependency path
                uint64_t cycle_desired = (static_cast<uint64_t>(thread_hash) << 32) | STATE_CYCLE;
                state_ref.compare_exchange_strong(current, cycle_desired);
                return false;
            }

            // Speculative Reevaluation using Thread ID as a Tie-Breaker (CAS)
            if (thread_hash > owner) {
                // Speculative takeover: Attempt to preempt evaluation of the lower priority thread
                uint64_t takeover_desired = (static_cast<uint64_t>(thread_hash) << 32) | STATE_COMPUTING;
                if (state_ref.compare_exchange_strong(current, takeover_desired)) {
                    // Preemption successful, fall through to compute
                } else {
                    continue; // State changed during CAS, loop again
                }
            } else {
                // Lower priority: yield and allow higher-priority thread to complete
                std::this_thread::yield();
                return true; // Return true speculatively to avoid deadlocking
            }
        }

        if (state == STATE_DIRTY) {
            uint64_t expected = current;
            uint64_t desired = (static_cast<uint64_t>(thread_hash) << 32) | STATE_COMPUTING;
            if (state_ref.compare_exchange_strong(expected, desired)) {
                break; // Lock acquired
            }
        }
    }

    // Process evaluation of AST expression nodes using std::span
    std::span<const ExpressionNode> cell_nodes = expr_nodes.subspan(cell.expr_start, cell.expr_count);
    
    bool success = true;
    double result = 0.0;

    if (cell_nodes.empty()) {
        result = cell.value; // Raw constant cell value
    } else {
        // Evaluate the root operations (simple binary AST evaluator)
        const ExpressionNode& root = cell_nodes[0];
        
        if (root.type == OP_ADD || root.type == OP_SUB || root.type == OP_MUL || root.type == OP_DIV) {
            if (cell_nodes.size() >= 3) {
                double left = EvaluateNode(cell_nodes[1], cells, expr_nodes, thread_hash, success);
                double right = EvaluateNode(cell_nodes[2], cells, expr_nodes, thread_hash, success);
                
                if (root.type == OP_ADD) result = left + right;
                else if (root.type == OP_SUB) result = left - right;
                else if (root.type == OP_MUL) result = left * right;
                else if (root.type == OP_DIV) result = (right != 0.0) ? (left / right) : 0.0;
            } else {
                success = false;
            }
        } else {
            result = EvaluateNode(root, cells, expr_nodes, thread_hash, success);
        }
    }

    if (success) {
        cell.value = result;
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
    EXPORT void RecalculateEngine(
        Cell* cells_ptr,
        int cells_count,
        const ExpressionNode* expr_nodes_ptr,
        int expr_nodes_count,
        int thread_count
    ) {
        // Zero-copy wrapping using std::span
        std::span<Cell> cells(cells_ptr, cells_count);
        std::span<const ExpressionNode> expr_nodes(expr_nodes_ptr, expr_nodes_count);

        if (thread_count <= 1) {
            uint32_t thread_hash = GetCurrentThreadIdHash();
            for (int i = 0; i < cells_count; ++i) {
                EvaluateAST(i, cells, expr_nodes, thread_hash);
            }
        } else {
            std::vector<std::thread> workers;
            for (int t = 0; t < thread_count; ++t) {
                workers.emplace_back([cells, expr_nodes, cells_count, t]() {
                    uint32_t thread_hash = GetCurrentThreadIdHash() + t;
                    for (int i = 0; i < cells_count; ++i) {
                        EvaluateAST(i, cells, expr_nodes, thread_hash);
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
