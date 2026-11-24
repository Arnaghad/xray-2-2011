cmake_minimum_required(VERSION 3.25)

set(OPCODE_SOURCES
    Ice/IceAABB.cpp
    Ice/IceContainer.cpp
    Ice/IceHPoint.cpp
    Ice/IceIndexedTriangle.cpp
    Ice/IceMatrix3x3.cpp
    Ice/IceMatrix4x4.cpp
    Ice/IceOBB.cpp
    Ice/IcePlane.cpp
    Ice/IcePoint.cpp
    Ice/IceRandom.cpp
    Ice/IceRay.cpp
    Ice/IceRevisitedRadix.cpp
    Ice/IceSegment.cpp
    Ice/IceTriangle.cpp
    Ice/IceUtils.cpp
    OPC_AABBCollider.cpp
    OPC_AABBTree.cpp
    OPC_BaseModel.cpp
    OPC_BoxPruning.cpp
    OPC_Collider.cpp
    OPC_Common.cpp
    OPC_HybridModel.cpp
    OPC_LSSCollider.cpp
    OPC_MeshInterface.cpp
    OPC_Model.cpp
    OPC_OBBCollider.cpp
    OPC_OptimizedTree.cpp
    OPC_Picking.cpp
    OPC_PlanesCollider.cpp
    OPC_RayCollider.cpp
    OPC_SphereCollider.cpp
    OPC_SweepAndPrune.cpp
    OPC_TreeBuilders.cpp
    OPC_TreeCollider.cpp
    OPC_VolumeCollider.cpp
    Opcode.cpp
)

add_library(opcode STATIC ${OPCODE_SOURCES})

target_include_directories(opcode PUBLIC ${CMAKE_CURRENT_SOURCE_DIR}/.. ${CMAKE_CURRENT_SOURCE_DIR})

target_link_libraries(opcode PUBLIC xray_core Boost::headers)

if(NOT MSVC)
    target_compile_definitions(opcode PRIVATE __noop=)
    target_compile_options(opcode PRIVATE -Wno-unknown-pragmas -fpermissive -fno-strict-aliasing)
else()
    target_compile_options(opcode PRIVATE -Wno-unknown-pragmas -fpermissive)
endif()
