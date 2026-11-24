#include "test_check.hpp"

#include <boost/version.hpp>
#include <boost/crc.hpp>
#include <boost/intrusive/set.hpp>
#include <asio/version.hpp>
#include <ogg/ogg.h>
#include <vorbis/codec.h>
#include <LinearMath/btScalar.h>

#include <cstring>
#include <iostream>

static_assert(BOOST_VERSION >= 109000, "Must use system Boost 1.90+, not bundled 1.48");
static_assert(ASIO_VERSION >= 103800, "Must use system Asio 1.38+");

namespace {

struct intrusive_node : public boost::intrusive::set_base_hook<> {
    int key = 0;
    explicit intrusive_node(int k) : key(k) {}
    friend bool operator<(intrusive_node const& a, intrusive_node const& b) {
        return a.key < b.key;
    }
};

} // namespace

int main() {
    boost::crc_32_type crc;
    char const payload[] = "xray-stage02";
    crc.process_bytes(payload, sizeof(payload) - 1);
    XRAY_TEST_CHECK(crc.checksum() != 0);

    boost::intrusive::set<intrusive_node> tree;
    intrusive_node n1(2), n2(1);
    tree.insert(n1);
    tree.insert(n2);
    XRAY_TEST_CHECK(tree.begin()->key == 1);
    tree.clear();

    XRAY_TEST_CHECK(btGetVersion() >= 325);
    XRAY_TEST_CHECK(vorbis_version_string() != nullptr);

    std::cout << "PASS: xray_dependency_single_source_contract (Boost=" << BOOST_LIB_VERSION
              << ", Asio=" << ASIO_VERSION
              << ", Bullet=" << btGetVersion()
              << ", Vorbis=" << vorbis_version_string() << ")\n";
    return 0;
}
