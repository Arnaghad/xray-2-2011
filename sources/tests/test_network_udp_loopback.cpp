#include "test_check.hpp"

#include <asio.hpp>

#include <array>
#include <iostream>
#include <string>

int main() {
    asio::io_context io;
    auto work = asio::make_work_guard(io);

    using udp = asio::ip::udp;
    auto const loopback = asio::ip::make_address_v4("127.0.0.1");

    // 1. Open, bind ephemeral loopback port, and verify async send/receive round-trip
    udp::socket receiver(io);
    udp::socket sender(io);

    std::error_code ec;
    receiver.open(udp::v4(), ec);
    XRAY_TEST_CHECK(!ec);
    receiver.bind(udp::endpoint(loopback, 0), ec);
    XRAY_TEST_CHECK(!ec);
    udp::endpoint const rx_ep = receiver.local_endpoint(ec);
    XRAY_TEST_CHECK(!ec && rx_ep.port() != 0);

    sender.open(udp::v4(), ec);
    XRAY_TEST_CHECK(!ec);
    sender.bind(udp::endpoint(loopback, 0), ec);
    XRAY_TEST_CHECK(!ec);

    std::array<char, 64> rx_buf{};
    udp::endpoint remote_ep;
    bool rx_done = false;
    std::size_t rx_bytes = 0;
    std::error_code rx_ec;

    receiver.async_receive_from(
        asio::buffer(rx_buf),
        remote_ep,
        [&](std::error_code const& recv_ec, std::size_t bytes) {
            rx_ec = recv_ec;
            rx_bytes = bytes;
            rx_done = true;
        }
    );

    std::string const payload = "xray-stage04-loopback";
    bool tx_done = false;
    std::size_t tx_bytes = 0;
    std::error_code tx_ec;
    sender.async_send_to(
        asio::buffer(payload),
        rx_ep,
        [&](std::error_code const& send_ec, std::size_t bytes) {
            tx_ec = send_ec;
            tx_bytes = bytes;
            tx_done = true;
        }
    );

    while (!rx_done || !tx_done) {
        io.run_one();
    }
    XRAY_TEST_CHECK(!tx_ec && tx_bytes == payload.size());
    XRAY_TEST_CHECK(!rx_ec && rx_bytes == payload.size());
    XRAY_TEST_CHECK(std::string(rx_buf.data(), rx_bytes) == payload);

    // 2. Bind error path: binding a second socket to the same exclusive loopback port must fail cleanly
    udp::socket conflict(io);
    conflict.open(udp::v4(), ec);
    XRAY_TEST_CHECK(!ec);
    conflict.set_option(udp::socket::reuse_address(false), ec);
    conflict.bind(rx_ep, ec);
    XRAY_TEST_CHECK(static_cast<bool>(ec));
    conflict.close(ec);

    // 3. Cancellation path: async_receive_from cancelled yields operation_aborted
    bool cancel_done = false;
    std::error_code cancel_ec;
    receiver.async_receive_from(
        asio::buffer(rx_buf),
        remote_ep,
        [&](std::error_code const& recv_ec, std::size_t) {
            cancel_ec = recv_ec;
            cancel_done = true;
        }
    );
    receiver.cancel(ec);
    XRAY_TEST_CHECK(!ec);
    while (!cancel_done) {
        io.run_one();
    }
    XRAY_TEST_CHECK(cancel_ec == asio::error::operation_aborted);

    // 4. Clean shutdown
    sender.close(ec);
    XRAY_TEST_CHECK(!ec);
    receiver.close(ec);
    XRAY_TEST_CHECK(!ec);
    work.reset();
    io.stop();

    std::cout << "PASS: xray_network_udp_loopback (port=" << rx_ep.port() << ")\n";
    return 0;
}
