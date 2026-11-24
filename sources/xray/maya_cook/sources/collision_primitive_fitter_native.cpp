#include "collision_primitive_fitter.hpp"

#include <algorithm>
#include <array>
#include <cmath>
#include <limits>
#include <numeric>
#include <random>
#include <stdexcept>
#include <vector>

namespace xray::maya_cook::detail {

namespace {

constexpr double kDefaultSinglePointRadius = 0.005;

inline vec3d add(vec3d const& a, vec3d const& b) {
    return {a.x + b.x, a.y + b.y, a.z + b.z};
}

inline vec3d sub(vec3d const& a, vec3d const& b) {
    return {a.x - b.x, a.y - b.y, a.z - b.z};
}

inline vec3d mul(vec3d const& a, double s) {
    return {a.x * s, a.y * s, a.z * s};
}

inline double dot(vec3d const& a, vec3d const& b) {
    return a.x * b.x + a.y * b.y + a.z * b.z;
}

inline vec3d cross(vec3d const& a, vec3d const& b) {
    return {
        a.y * b.z - a.z * b.y,
        a.z * b.x - a.x * b.z,
        a.x * b.y - a.y * b.x,
    };
}

inline double sq_len(vec3d const& v) {
    return dot(v, v);
}

inline vec3d normalize_or(vec3d const& v, vec3d const& fallback) {
    double const l2 = sq_len(v);
    if (!(l2 > 1e-24) || !std::isfinite(l2)) {
        return fallback;
    }
    return mul(v, 1.0 / std::sqrt(l2));
}

// --- Native Welzl Minimal Enclosing Sphere ---

struct sphere_sq {
    vec3d center{};
    double r_sq = 0.0;
};

inline bool contains_sq(sphere_sq const& s, vec3d const& p, double eps_scale = 1e-11) {
    double const d2 = sq_len(sub(p, s.center));
    return d2 <= s.r_sq * (1.0 + eps_scale) + 1e-18;
}

sphere_sq sphere_from_2(vec3d const& a, vec3d const& b) {
    vec3d const c = mul(add(a, b), 0.5);
    double const r2 = 0.25 * sq_len(sub(b, a));
    return {c, r2};
}

bool try_sphere_from_3(vec3d const& a, vec3d const& b, vec3d const& c, sphere_sq& out) {
    vec3d const u = sub(b, a);
    vec3d const v = sub(c, a);
    vec3d const w = cross(u, v);
    double const w2 = sq_len(w);
    double const uv2 = sq_len(u) * sq_len(v);
    if (!(w2 > 1e-14 * uv2) || w2 <= 1e-30) {
        return false;
    }
    vec3d const num = add(mul(cross(w, u), sq_len(v)), mul(cross(v, w), sq_len(u)));
    vec3d const offset = mul(num, 1.0 / (2.0 * w2));
    out = {add(a, offset), sq_len(offset)};
    return std::isfinite(out.center.x) && std::isfinite(out.r_sq);
}

bool try_sphere_from_4(
    vec3d const& a, vec3d const& b, vec3d const& c, vec3d const& d, sphere_sq& out
) {
    vec3d const u = sub(b, a);
    vec3d const v = sub(c, a);
    vec3d const t = sub(d, a);
    vec3d const s1 = cross(v, t);
    vec3d const s2 = cross(t, u);
    vec3d const s3 = cross(u, v);
    double const det = dot(u, s1);
    double const scale = std::max({sq_len(u), sq_len(v), sq_len(t), 1e-30});
    if (std::abs(det) <= 1e-12 * std::pow(scale, 1.5)) {
        return false;
    }
    vec3d const num = add(
        add(mul(s1, sq_len(u)), mul(s2, sq_len(v))),
        mul(s3, sq_len(t))
    );
    vec3d const offset = mul(num, 1.0 / (2.0 * det));
    out = {add(a, offset), sq_len(offset)};
    return std::isfinite(out.center.x) && std::isfinite(out.r_sq);
}

sphere_sq min_sphere_small(std::span<vec3d const> b) {
    if (b.empty()) {
        return {};
    }
    if (b.size() == 1) {
        return {b[0], 0.0};
    }
    if (b.size() == 2) {
        return sphere_from_2(b[0], b[1]);
    }

    sphere_sq best{};
    double best_r2 = std::numeric_limits<double>::infinity();
    auto consider = [&](sphere_sq const& cand) {
        if (cand.r_sq >= best_r2) {
            return;
        }
        for (auto const& pt : b) {
            if (!contains_sq(cand, pt, 1e-10)) {
                return;
            }
        }
        best = cand;
        best_r2 = cand.r_sq;
    };

    for (std::size_t i = 0; i < b.size(); ++i) {
        for (std::size_t j = i + 1; j < b.size(); ++j) {
            consider(sphere_from_2(b[i], b[j]));
        }
    }
    for (std::size_t i = 0; i < b.size(); ++i) {
        for (std::size_t j = i + 1; j < b.size(); ++j) {
            for (std::size_t k = j + 1; k < b.size(); ++k) {
                sphere_sq s{};
                if (try_sphere_from_3(b[i], b[j], b[k], s)) {
                    consider(s);
                }
            }
        }
    }
    if (b.size() == 4) {
        sphere_sq s{};
        if (try_sphere_from_4(b[0], b[1], b[2], b[3], s)) {
            consider(s);
        }
    }
    return std::isfinite(best_r2) ? best : sphere_from_2(b[0], b[1]);
}

// Bounded-depth (<= 3 nested loops) iterative Welzl over shuffled points.
sphere_sq welzl_3(std::span<vec3d const> pts, vec3d const& q1, vec3d const& q2, vec3d const& q3) {
    std::array<vec3d, 3> const base{q1, q2, q3};
    sphere_sq ball = min_sphere_small(base);
    for (std::size_t i = 0; i < pts.size(); ++i) {
        if (!contains_sq(ball, pts[i])) {
            std::array<vec3d, 4> const quad{q1, q2, q3, pts[i]};
            ball = min_sphere_small(quad);
        }
    }
    return ball;
}

sphere_sq welzl_2(std::span<vec3d const> pts, vec3d const& q1, vec3d const& q2) {
    sphere_sq ball = sphere_from_2(q1, q2);
    for (std::size_t i = 0; i < pts.size(); ++i) {
        if (!contains_sq(ball, pts[i])) {
            ball = welzl_3(pts.subspan(0, i), q1, q2, pts[i]);
        }
    }
    return ball;
}

sphere_sq welzl_1(std::span<vec3d const> pts, vec3d const& q1) {
    sphere_sq ball{q1, 0.0};
    for (std::size_t i = 0; i < pts.size(); ++i) {
        if (!contains_sq(ball, pts[i])) {
            ball = welzl_2(pts.subspan(0, i), q1, pts[i]);
        }
    }
    return ball;
}

// --- Native PCA + 2D Rotating Calipers OBB ---

void make_right_handed(vec3d& u0, vec3d& u1, vec3d& u2) {
    u0 = normalize_or(u0, {1.0, 0.0, 0.0});
    u1 = normalize_or(sub(u1, mul(u0, dot(u0, u1))), {0.0, 1.0, 0.0});
    if (std::abs(dot(u0, u1)) > 0.1) {
        vec3d const alt = std::abs(u0.y) < 0.9 ? vec3d{0.0, 1.0, 0.0} : vec3d{0.0, 0.0, 1.0};
        u1 = normalize_or(sub(alt, mul(u0, dot(u0, alt))), {0.0, 1.0, 0.0});
    }
    u2 = normalize_or(cross(u0, u1), {0.0, 0.0, 1.0});
}

void jacobi_eigen_3x3(double a[3][3], vec3d basis[3]) {
    double v[3][3] = {
        {1.0, 0.0, 0.0},
        {0.0, 1.0, 0.0},
        {0.0, 0.0, 1.0},
    };
    for (int iter = 0; iter < 32; ++iter) {
        int p = 0;
        int q = 1;
        double max_off = std::abs(a[0][1]);
        if (std::abs(a[0][2]) > max_off) {
            max_off = std::abs(a[0][2]);
            p = 0;
            q = 2;
        }
        if (std::abs(a[1][2]) > max_off) {
            max_off = std::abs(a[1][2]);
            p = 1;
            q = 2;
        }
        if (max_off <= 1e-15) {
            break;
        }
        double const app = a[p][p];
        double const aqq = a[q][q];
        double const apq = a[p][q];
        double const tau = (aqq - app) / (2.0 * apq);
        double const t = tau >= 0.0
            ? 1.0 / (tau + std::sqrt(1.0 + tau * tau))
            : -1.0 / (-tau + std::sqrt(1.0 + tau * tau));
        double const c = 1.0 / std::sqrt(1.0 + t * t);
        double const s = t * c;

        a[p][p] = app - t * apq;
        a[q][q] = aqq + t * apq;
        a[p][q] = 0.0;
        a[q][p] = 0.0;

        int const r = 3 - p - q;
        double const arp = a[r][p];
        double const arq = a[r][q];
        a[r][p] = a[p][r] = c * arp - s * arq;
        a[r][q] = a[q][r] = s * arp + c * arq;

        for (int row = 0; row < 3; ++row) {
            double const vrp = v[row][p];
            double const vrq = v[row][q];
            v[row][p] = c * vrp - s * vrq;
            v[row][q] = s * vrp + c * vrq;
        }
    }

    std::array<int, 3> order{0, 1, 2};
    std::sort(order.begin(), order.end(), [&](int i, int j) {
        return a[i][i] > a[j][j];
    });
    for (int k = 0; k < 3; ++k) {
        int const col = order[k];
        basis[k] = {v[0][col], v[1][col], v[2][col]};
    }
    make_right_handed(basis[0], basis[1], basis[2]);
}

fitted_obb evaluate_frame(std::span<vec3d const> points, vec3d u0, vec3d u1, vec3d u2) {
    make_right_handed(u0, u1, u2);
    double min0 = std::numeric_limits<double>::infinity();
    double min1 = std::numeric_limits<double>::infinity();
    double min2 = std::numeric_limits<double>::infinity();
    double max0 = -std::numeric_limits<double>::infinity();
    double max1 = -std::numeric_limits<double>::infinity();
    double max2 = -std::numeric_limits<double>::infinity();

    for (auto const& p : points) {
        double const d0 = dot(p, u0);
        double const d1 = dot(p, u1);
        double const d2 = dot(p, u2);
        min0 = std::min(min0, d0);
        max0 = std::max(max0, d0);
        min1 = std::min(min1, d1);
        max1 = std::max(max1, d1);
        min2 = std::min(min2, d2);
        max2 = std::max(max2, d2);
    }

    double const c0 = 0.5 * (min0 + max0);
    double const c1 = 0.5 * (min1 + max1);
    double const c2 = 0.5 * (min2 + max2);

    fitted_obb box;
    box.center = add(add(mul(u0, c0), mul(u1, c1)), mul(u2, c2));
    box.axis[0] = u0;
    box.axis[1] = u1;
    box.axis[2] = u2;
    box.extent[0] = 0.5 * (max0 - min0);
    box.extent[1] = 0.5 * (max1 - min1);
    box.extent[2] = 0.5 * (max2 - min2);
    return box;
}

double box_score(fitted_obb const& box) {
    // Soft floor preserves strict comparison across planar (1 zero extent) and collinear (2 zero extents) cases.
    double const floor_eps = 1e-8;
    return std::max(box.extent[0], floor_eps) *
        std::max(box.extent[1], floor_eps) *
        std::max(box.extent[2], floor_eps);
}

struct pt2d {
    double x = 0.0;
    double y = 0.0;
};

double cross2d(pt2d const& o, pt2d const& a, pt2d const& b) {
    return (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
}

std::vector<pt2d> convex_hull_2d(std::vector<pt2d> pts) {
    std::sort(pts.begin(), pts.end(), [](pt2d const& a, pt2d const& b) {
        return a.x < b.x || (a.x == b.x && a.y < b.y);
    });
    pts.erase(std::unique(pts.begin(), pts.end(), [](pt2d const& a, pt2d const& b) {
        return std::abs(a.x - b.x) <= 1e-12 && std::abs(a.y - b.y) <= 1e-12;
    }), pts.end());

    if (pts.size() <= 2) {
        return pts;
    }

    std::vector<pt2d> hull;
    hull.reserve(pts.size() * 2);
    for (auto const& p : pts) {
        while (hull.size() >= 2 && cross2d(hull[hull.size() - 2], hull.back(), p) <= 0.0) {
            hull.pop_back();
        }
        hull.push_back(p);
    }
    std::size_t const lower_sz = hull.size() + 1;
    for (std::size_t k = pts.size(); k-- > 0;) {
        auto const& p = pts[k];
        while (hull.size() >= lower_sz && cross2d(hull[hull.size() - 2], hull.back(), p) <= 0.0) {
            hull.pop_back();
        }
        hull.push_back(p);
    }
    if (!hull.empty()) {
        hull.pop_back();
    }
    return hull;
}

void best_2d_aligned_axes(
    std::span<vec3d const> points,
    vec3d const& v0,
    vec3d const& v1,
    vec3d const& normal_axis,
    vec3d& out_a0,
    vec3d& out_a1
) {
    std::vector<pt2d> proj;
    proj.reserve(points.size());
    for (auto const& p : points) {
        proj.push_back({dot(p, v0), dot(p, v1)});
    }
    std::vector<pt2d> const hull = convex_hull_2d(std::move(proj));
    if (hull.size() < 2) {
        out_a0 = v0;
        out_a1 = v1;
        return;
    }

    if (hull.size() == 2) {
        double dx = hull[1].x - hull[0].x;
        double dy = hull[1].y - hull[0].y;
        double const len = std::hypot(dx, dy);
        if (len > 1e-12) {
            dx /= len;
            dy /= len;
            out_a0 = add(mul(v0, dx), mul(v1, dy));
            out_a1 = add(mul(v0, -dy), mul(v1, dx));
            return;
        }
    }

    double best_area = std::numeric_limits<double>::infinity();
    vec3d best0 = v0;
    vec3d best1 = v1;

    for (std::size_t i = 0; i < hull.size(); ++i) {
        pt2d const& a = hull[i];
        pt2d const& b = hull[(i + 1) % hull.size()];
        double dx = b.x - a.x;
        double dy = b.y - a.y;
        double const len = std::hypot(dx, dy);
        if (!(len > 1e-12)) {
            continue;
        }
        dx /= len;
        dy /= len;
        double const nx = -dy;
        double const ny = dx;

        double min_u = std::numeric_limits<double>::infinity();
        double max_u = -std::numeric_limits<double>::infinity();
        double min_v = std::numeric_limits<double>::infinity();
        double max_v = -std::numeric_limits<double>::infinity();
        for (auto const& h : hull) {
            double const pu = h.x * dx + h.y * dy;
            double const pv = h.x * nx + h.y * ny;
            min_u = std::min(min_u, pu);
            max_u = std::max(max_u, pu);
            min_v = std::min(min_v, pv);
            max_v = std::max(max_v, pv);
        }
        double const area = std::max(max_u - min_u, 1e-9) * std::max(max_v - min_v, 1e-9);
        if (area < best_area) {
            best_area = area;
            best0 = add(mul(v0, dx), mul(v1, dy));
            best1 = add(mul(v0, nx), mul(v1, ny));
        }
    }

    vec3d tmp_n = normal_axis;
    make_right_handed(best0, best1, tmp_n);
    out_a0 = best0;
    out_a1 = best1;
}

} // namespace

fitted_sphere fit_bounding_sphere_native(std::span<vec3d const> points) {
    if (points.empty()) {
        throw std::runtime_error("fit_bounding_sphere_native requires at least one point");
    }
    if (points.size() == 1) {
        return {points[0], kDefaultSinglePointRadius};
    }

    std::vector<vec3d> shuffled(points.begin(), points.end());
    std::mt19937_64 rng(0x9e3779b97f4a7c15ULL);
    std::shuffle(shuffled.begin(), shuffled.end(), rng);

    sphere_sq ball{shuffled[0], 0.0};
    for (std::size_t i = 1; i < shuffled.size(); ++i) {
        if (!contains_sq(ball, shuffled[i])) {
            ball = welzl_1(std::span<vec3d const>(shuffled.data(), i), shuffled[i]);
        }
    }

    double max_r_sq = ball.r_sq;
    for (auto const& p : points) {
        max_r_sq = std::max(max_r_sq, sq_len(sub(p, ball.center)));
    }
    double const radius = std::sqrt(max_r_sq) * (1.0 + 1e-9);
    return {ball.center, radius};
}

fitted_obb fit_oriented_box_native(std::span<vec3d const> points) {
    if (points.empty()) {
        throw std::runtime_error("fit_oriented_box_native requires at least one point");
    }
    if (points.size() == 1) {
        fitted_obb single;
        single.center = points[0];
        single.extent[0] = kDefaultSinglePointRadius;
        single.extent[1] = kDefaultSinglePointRadius;
        single.extent[2] = kDefaultSinglePointRadius;
        return single;
    }

    vec3d mean{};
    for (auto const& p : points) {
        mean = add(mean, p);
    }
    double const inv_n = 1.0 / static_cast<double>(points.size());
    mean = mul(mean, inv_n);

    double cov[3][3]{};
    for (auto const& p : points) {
        vec3d const d = sub(p, mean);
        cov[0][0] += d.x * d.x;
        cov[0][1] += d.x * d.y;
        cov[0][2] += d.x * d.z;
        cov[1][1] += d.y * d.y;
        cov[1][2] += d.y * d.z;
        cov[2][2] += d.z * d.z;
    }
    for (int i = 0; i < 3; ++i) {
        for (int j = i; j < 3; ++j) {
            cov[i][j] *= inv_n;
            cov[j][i] = cov[i][j];
        }
    }

    vec3d pca[3];
    jacobi_eigen_3x3(cov, pca);

    fitted_obb best = evaluate_frame(
        points, {1.0, 0.0, 0.0}, {0.0, 1.0, 0.0}, {0.0, 0.0, 1.0}
    );
    double best_score = box_score(best);

    auto try_frame = [&](vec3d const& a0, vec3d const& a1, vec3d const& a2) {
        fitted_obb const cand = evaluate_frame(points, a0, a1, a2);
        double const s = box_score(cand);
        if (s < best_score) {
            best_score = s;
            best = cand;
        }
    };

    try_frame(pca[0], pca[1], pca[2]);

    // Project along each PCA axis (especially the smallest-variance axis for coplanar/prismatic clouds)
    // and refine the remaining 2 axes via 2D rotating calipers over the convex hull.
    for (int k = 0; k < 3; ++k) {
        vec3d const& fixed = pca[k];
        vec3d const& v0 = pca[(k + 1) % 3];
        vec3d const& v1 = pca[(k + 2) % 3];
        vec3d a0, a1;
        best_2d_aligned_axes(points, v0, v1, fixed, a0, a1);
        try_frame(a0, a1, fixed);
    }

    return best;
}

} // namespace xray::maya_cook::detail
