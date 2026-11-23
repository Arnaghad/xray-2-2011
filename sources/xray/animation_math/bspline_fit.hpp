#pragma once

#include <algorithm>
#include <array>
#include <cmath>
#include <cstddef>
#include <limits>
#include <stdexcept>
#include <utility>
#include <vector>

namespace xray::animation_math {

/// Evaluate clamped open cubic (order 4) Cox-de Boor B-spline at parameter `t`.
/// `knots` must have size `coefs.size() + 4`.
inline float evaluate_cubic_bspline(
    std::vector<float> const& knots,
    std::vector<float> const& coefs,
    float t
) {
    std::size_t const n = coefs.size();
    if (n == 0) return 0.0f;
    if (knots.size() < n + 4) {
        throw std::runtime_error("B-spline knot vector shorter than n + 4");
    }
    constexpr std::size_t p = 3;
    float const t_min = knots[p];
    float const t_max = knots[n];
    if (t <= t_min) return coefs.front();
    if (t >= t_max) return coefs.back();

    std::size_t k = p;
    for (std::size_t i = p; i < n; ++i) {
        if (knots[i] <= t && t < knots[i + 1]) {
            k = i;
            break;
        }
    }

    std::array<double, 4> d{};
    for (std::size_t j = 0; j <= p; ++j) {
        d[j] = static_cast<double>(coefs[k - p + j]);
    }
    for (std::size_t r = 1; r <= p; ++r) {
        for (std::size_t j = p; j >= r; --j) {
            double const left = knots[k - p + j];
            double const right = knots[k + 1 + j - r];
            double const denom = right - left;
            double const alpha = (denom > 1e-12) ? ((double(t) - left) / denom) : 0.0;
            d[j] = (1.0 - alpha) * d[j - 1] + alpha * d[j];
        }
    }
    return static_cast<float>(d[p]);
}

namespace bspline_detail {

inline void basis_functions(
    std::vector<double> const& knots,
    std::size_t n,
    double t,
    std::size_t& out_span,
    std::array<double, 4>& out_N
) {
    constexpr std::size_t p = 3;
    if (t >= knots[n]) {
        out_span = n - 1;
        out_N = {0.0, 0.0, 0.0, 1.0};
        return;
    }
    if (t <= knots[p]) {
        out_span = p;
        out_N = {1.0, 0.0, 0.0, 0.0};
        return;
    }

    std::size_t lo = p;
    std::size_t hi = n;
    std::size_t mid = (lo + hi) / 2;
    while (t < knots[mid] || t >= knots[mid + 1]) {
        if (t < knots[mid]) hi = mid;
        else lo = mid;
        mid = (lo + hi) / 2;
    }
    out_span = mid;

    std::array<double, 4> left{}, right{};
    out_N[0] = 1.0;
    for (std::size_t j = 1; j <= p; ++j) {
        left[j] = t - knots[out_span + 1 - j];
        right[j] = knots[out_span + j] - t;
        double saved = 0.0;
        for (std::size_t r = 0; r < j; ++r) {
            double denom = right[r + 1] + left[j - r];
            double temp = (denom > 1e-15) ? (out_N[r] / denom) : 0.0;
            out_N[r] = saved + right[r + 1] * temp;
            saved = left[j - r] * temp;
        }
        out_N[j] = saved;
    }
}

inline std::vector<float> solve_least_squares(
    std::vector<double> const& knots,
    std::vector<double> const& sample_t,
    std::vector<float> const& sample_y,
    std::size_t n
) {
    std::size_t const m = sample_t.size();
    std::vector<double> ata(n * n, 0.0);
    std::vector<double> atb(n, 0.0);

    for (std::size_t row = 0; row < m; ++row) {
        std::size_t span = 0;
        std::array<double, 4> N{};
        basis_functions(knots, n, sample_t[row], span, N);
        double const w = (row == 0 || row + 1 == m) ? 100.0 : 1.0;
        double const y = static_cast<double>(sample_y[row]);
        std::size_t const base = span - 3;
        for (std::size_t a = 0; a < 4; ++a) {
            std::size_t const ia = base + a;
            atb[ia] += w * N[a] * y;
            for (std::size_t b = 0; b < 4; ++b) {
                std::size_t const ib = base + b;
                ata[ia * n + ib] += w * N[a] * N[b];
            }
        }
    }
    for (std::size_t i = 0; i < n; ++i) {
        ata[i * n + i] += 1e-10;
    }

    // Gaussian elimination with partial pivoting
    for (std::size_t col = 0; col < n; ++col) {
        std::size_t piv = col;
        double best = std::abs(ata[col * n + col]);
        for (std::size_t r = col + 1; r < n; ++r) {
            double v = std::abs(ata[r * n + col]);
            if (v > best) {
                best = v;
                piv = r;
            }
        }
        if (piv != col) {
            for (std::size_t c = col; c < n; ++c) {
                std::swap(ata[col * n + c], ata[piv * n + c]);
            }
            std::swap(atb[col], atb[piv]);
        }
        double diag = ata[col * n + col];
        if (std::abs(diag) < 1e-15) diag = (diag >= 0.0) ? 1e-15 : -1e-15;
        for (std::size_t r = col + 1; r < n; ++r) {
            double f = ata[r * n + col] / diag;
            if (f == 0.0) continue;
            for (std::size_t c = col; c < n; ++c) {
                ata[r * n + c] -= f * ata[col * n + c];
            }
            atb[r] -= f * atb[col];
        }
    }

    std::vector<float> coefs(n, 0.0f);
    for (std::size_t idx = n; idx-- > 0;) {
        double sum = atb[idx];
        for (std::size_t c = idx + 1; c < n; ++c) {
            sum -= ata[idx * n + c] * static_cast<double>(coefs[c]);
        }
        double diag = ata[idx * n + idx];
        coefs[idx] = static_cast<float>(sum / (std::abs(diag) < 1e-15 ? 1e-15 : diag));
    }
    // Pin endpoints strictly to start/end sample values
    coefs.front() = sample_y.front();
    coefs.back() = sample_y.back();
    return coefs;
}

} // namespace bspline_detail

/// Result formatted directly for X-Ray `.clip` storage:
/// `pairs` contains `{t[0], c[0]} .. {t[n-1], c[n-1]}, {t[n], -1000000000.0f}`.
struct fitted_bspline_channel {
    std::vector<std::pair<float, float>> clip_pairs;
    std::vector<float> full_knots;
    std::vector<float> coefs;
};

/// Fit a clamped open cubic B-spline to uniform samples `samples` with spacing `dt`
/// within max pointwise tolerance `epsilon`.
inline fitted_bspline_channel fit_cubic_bspline_channel(
    std::vector<float> const& raw_samples,
    float dt,
    float epsilon
) {
    if (raw_samples.empty()) {
        throw std::runtime_error("Cannot fit B-spline to empty sample channel");
    }
    if (!(dt > 0.0f) || !(epsilon > 0.0f) || !std::isfinite(dt) || !std::isfinite(epsilon)) {
        throw std::runtime_error("B-spline dt and epsilon must be positive");
    }

    std::vector<float> samples = raw_samples;
    if (samples.size() == 1) {
        samples.push_back(samples.front());
    }

    float min_v = samples[0], max_v = samples[0];
    for (float s : samples) {
        if (!std::isfinite(s)) throw std::runtime_error("Non-finite animation sample");
        min_v = std::min(min_v, s);
        max_v = std::max(max_v, s);
    }

    double const t_end = static_cast<double>(samples.size() - 1) * static_cast<double>(dt);
    if (!std::isfinite(float(t_end))) throw std::runtime_error("Animation time range exceeds float storage");

    // Fast path for constant / flat channel matching GSC `.clip` convention
    if ((max_v - min_v) * 0.5f <= epsilon) {
        float const mid = 0.5f * (min_v + max_v);
        // The midpoint bounds the error by half the range. Choosing the first
        // sample when the total range exceeds epsilon can violate precision.
        float const val = (std::abs(mid) < 1e-6f && std::max(std::abs(min_v), std::abs(max_v)) <= epsilon) ? 0.0f : mid;
        fitted_bspline_channel out;
        out.coefs.assign(4, val);
        out.full_knots = {0.0f, 0.0f, 0.0f, 0.0f, float(t_end), float(t_end), float(t_end), float(t_end)};
        for (int i = 0; i < 4; ++i) {
            out.clip_pairs.emplace_back(0.0f, val);
        }
        out.clip_pairs.emplace_back(float(t_end), -1000000000.0f);
        return out;
    }

    std::vector<double> sample_t(samples.size());
    for (std::size_t i = 0; i < samples.size(); ++i) {
        sample_t[i] = static_cast<double>(i) * static_cast<double>(dt);
    }

    std::vector<double> knots = {0.0, 0.0, 0.0, 0.0, t_end, t_end, t_end, t_end};
    std::vector<float> coefs;
    std::size_t const max_cvs = std::max<std::size_t>(samples.size() + 4, 8);

    for (std::size_t iter = 0; iter < samples.size() * 2 + 8; ++iter) {
        std::size_t n = knots.size() - 4;
        coefs = bspline_detail::solve_least_squares(knots, sample_t, samples, n);

        std::vector<float> fknots(knots.begin(), knots.end());
        double worst_err = 0.0;
        std::size_t worst_idx = 0;
        for (std::size_t i = 0; i < samples.size(); ++i) {
            float approx = evaluate_cubic_bspline(fknots, coefs, static_cast<float>(sample_t[i]));
            double err = std::abs(double(approx) - double(samples[i]));
            if (err > worst_err) {
                worst_err = err;
                worst_idx = i;
            }
        }

        if (worst_err <= static_cast<double>(epsilon) || n >= max_cvs) {
            break;
        }

        // Insert a knot in the span around worst_idx; keep knots strictly non-degenerate
        double target = sample_t[worst_idx];
        auto it = std::upper_bound(knots.begin() + 4, knots.end() - 4, target);
        double left_k = *(it - 1);
        double right_k = *it;
        double candidate = target;
        double min_gap = std::max(1e-5, t_end * 1e-6);
        if (candidate - left_k < min_gap || right_k - candidate < min_gap) {
            candidate = 0.5 * (left_k + right_k);
        }
        if (candidate - left_k < min_gap || right_k - candidate < min_gap) {
            // Find widest span with error > epsilon
            std::size_t best_span_it = 0;
            double best_score = -1.0;
            for (std::size_t k = 3; k + 4 < knots.size(); ++k) {
                double len = knots[k + 1] - knots[k];
                if (len <= min_gap * 2.0) continue;
                double span_err = 0.0;
                for (std::size_t s = 0; s < samples.size(); ++s) {
                    if (sample_t[s] >= knots[k] && sample_t[s] <= knots[k + 1]) {
                        float approx = evaluate_cubic_bspline(fknots, coefs, static_cast<float>(sample_t[s]));
                        span_err = std::max(span_err, std::abs(double(approx) - double(samples[s])));
                    }
                }
                double score = span_err * len;
                if (score > best_score) {
                    best_score = score;
                    best_span_it = k;
                }
            }
            if (best_span_it == 0 && knots[4] - knots[3] <= min_gap * 2.0) {
                break;
            }
            candidate = 0.5 * (knots[best_span_it] + knots[best_span_it + 1]);
            it = knots.begin() + static_cast<std::ptrdiff_t>(best_span_it + 1);
        }
        knots.insert(it, candidate);
    }

    fitted_bspline_channel out;
    out.full_knots.assign(knots.begin(), knots.end());
    out.coefs = std::move(coefs);
    auto within_precision = [&]() {
        for (std::size_t i = 0; i < samples.size(); ++i) {
            float const value = evaluate_cubic_bspline(out.full_knots, out.coefs, float(sample_t[i]));
            if (!std::isfinite(value) || std::abs(double(value)-samples[i]) > epsilon) return false;
        }
        return true;
    };
    if (!within_precision()) {
        // Adaptive least squares can reach its control-point/conditioning limit
        // before meeting precision. A chain of cubic Hermite Bezier spans retains
        // every sample exactly, with matching estimated derivatives at each join.
        // Triple interior knots are supported by the engine's cubic converter.
        out.full_knots.clear();
        out.coefs.clear();
        auto delta = [&](std::size_t i) -> double {
            if (i == 0) return double(samples[1])-samples[0];
            if (i+1 == samples.size()) return double(samples[i])-samples[i-1];
            return (double(samples[i+1])-samples[i-1])*0.5;
        };
        for (std::size_t i = 0; i < samples.size(); ++i) {
            unsigned const multiplicity = (i == 0 || i+1 == samples.size()) ? 4 : 3;
            out.full_knots.insert(out.full_knots.end(), multiplicity, float(sample_t[i]));
        }
        out.coefs.push_back(samples.front());
        for (std::size_t i = 0; i+1 < samples.size(); ++i) {
            out.coefs.push_back(float(double(samples[i])+delta(i)/3.0));
            out.coefs.push_back(float(double(samples[i+1])-delta(i+1)/3.0));
            out.coefs.push_back(samples[i+1]);
        }
        if (!within_precision()) throw std::runtime_error("Cubic interpolation cannot meet animation precision");
    }
    std::size_t const n = out.coefs.size();
    out.clip_pairs.reserve(n + 1);
    for (std::size_t i = 0; i < n; ++i) {
        out.clip_pairs.emplace_back(out.full_knots[i], out.coefs[i]);
    }
    out.clip_pairs.emplace_back(out.full_knots[n], -1000000000.0f);
    return out;
}

} // namespace xray::animation_math

